using System.Text.Json;
using System.Text.Unicode;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Core.Tenancy;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Records;
using Axis.Server.Tenancy;
using Axis.Server.Users;

namespace Axis.Server.Processes;

/// <summary>
/// The endpoints for a user who may operate on the instances of a process. A cancel at
/// <c>POST /api/apps/{app}/processes/{process}/instances/{id}/cancel</c> ends a <c>running</c> or
/// <c>waiting</c> instance as <c>cancelled</c> and answers 200 with the instance. A request is
/// checked in order: session (401), application and process (404), the user's <c>cancel</c> grant
/// on the process (403), content type (415), then the body (400), which is exactly
/// <c>{ "reason": "&lt;text&gt;" }</c> with a reason of 1 to 500 characters. An <c>id</c> that is
/// not a hyphenated UUID is a 404, as is an instance of another process or one whose subject
/// record the user may not read. Then one transaction runs <see cref="ProcessCancelCommand"/>,
/// which answers 409, before it looks for the instance, when a <c>read</c> filter of the user reads
/// a computed field that is being recomputed, 409 for an instance that is not running or waiting, and 409 with the call's id
/// in <c>callId</c> when a call may have reached the other system. A cancel that is refused
/// writes nothing. The cancel takes no start lock, because it writes no record and starts no
/// trigger. A retry at <c>POST /api/apps/{app}/processes/{process}/instances/{id}/retry</c> sends a
/// <c>failed</c> instance back to <c>running</c> on its failed step and answers 200 with the instance.
/// It is checked in the same order with the user's <c>retry</c> grant and the same body. Then one
/// transaction takes the application's start lock shared first, because the retry makes an instance
/// active again, and runs <see cref="ProcessRetryCommand"/>. It answers the same 409 for a
/// <c>read</c> filter on a field that is being recomputed, before it looks for the instance, 409
/// for an instance that is not failed, 409 when another instance of the process is running or waiting for the record, and
/// 409 when an applied migration changed a column that the instance's pinned release uses.
/// When the active release is no longer the one resolved, the retry runs again from resolution on
/// the new release, and answers 503 after three attempts. A retry that is refused writes nothing.
/// The problem titles never contain text from the request.
/// </summary>
internal static class ProcessOperatorEndpoints
{
    private const string ReasonProperty = "reason";

    private const int MaxReasonLength = 500;

    // The record API's messages for the same problems.
    private const string MalformedBody = "Must be a JSON object.";

    private const string UnknownProperty = "Unknown property.";

    private const string DuplicateProperty = "Duplicate property.";

    public static void MapProcessOperatorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/apps/{app}/processes/{process}/instances/{id}/cancel", CancelAsync);
        endpoints.MapPost("/api/apps/{app}/processes/{process}/instances/{id}/retry", RetryAsync);
    }

    private static async Task<IResult> CancelAsync(
        string app,
        string process,
        string id,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return NotFound("No application is active under this name.");
        }

        var application = active.Model;
        if (!application.TryGetProcess(process, out var model))
        {
            return NotFound("The application has no process with this name.");
        }

        if (!PolicyEvaluator.AllowsCancel(application, user.Roles, model))
        {
            return Grants.Forbidden();
        }

        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var (reason, errors) = ReadReason(await ReadBodyAsync(request, cancellationToken));
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var instanceId))
        {
            return InstanceNotFound();
        }

        if (!application.TryGetEntity(model.Entity.Name, out var entity))
        {
            throw new InvalidOperationException($"The subject entity '{model.Entity.Name}' of process '{model.Name}' is not in the model.");
        }

        var read = PolicyEvaluator.Access(application, user.Roles, entity, PolicyAction.Read);
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var result = await ProcessCancelCommand.CancelAsync(
            connection,
            transaction,
            application,
            model,
            entity,
            instanceId,
            read,
            user.Id,
            user.DisplayName,
            reason!,
            loggerFactory,
            cancellationToken);
        if (result.Outcome != ProcessCancelOutcome.Cancelled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return result.Outcome switch
            {
                ProcessCancelOutcome.NotFound => InstanceNotFound(),
                ProcessCancelOutcome.PendingFilter => Grants.PendingFilter(),
                ProcessCancelOutcome.NotActive => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The instance is not running or waiting."),
                _ => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A call of this instance may have reached the other system. It must settle or be resolved first.",
                    extensions: new Dictionary<string, object?> { ["callId"] = result.CallId }),
            };
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Json(
            new ProcessInstanceResponse(instanceId, model.Name, result.SubjectId!.Value, result.ReleaseId!.Value, ProcessStarts.Cancelled));
    }

    private static async Task<IResult> RetryAsync(
        string app,
        string process,
        string id,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        // A release change under the start lock sends the request back to resolution, so every check runs on the new release.
        for (var attempt = 0; attempt < ReleaseRetry.MaxAttempts; attempt++)
        {
            if (await RetryOnceAsync(app, process, id, request, user, resolver, database, tenants, loggerFactory, cancellationToken) is { } result)
            {
                return result;
            }
        }

        return ReleaseRetry.Changed();
    }

    // Null when the active release changed while the retry waited for the start lock. Nothing was written.
    private static async Task<IResult?> RetryOnceAsync(
        string app,
        string process,
        string id,
        HttpRequest request,
        CurrentUserResponse user,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return NotFound("No application is active under this name.");
        }

        var application = active.Model;
        if (!application.TryGetProcess(process, out var model))
        {
            return NotFound("The application has no process with this name.");
        }

        if (!PolicyEvaluator.AllowsRetry(application, user.Roles, model))
        {
            return Grants.Forbidden();
        }

        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var (reason, errors) = ReadReason(await ReadBodyAsync(request, cancellationToken));
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var instanceId))
        {
            return InstanceNotFound();
        }

        if (!application.TryGetEntity(model.Entity.Name, out var entity))
        {
            throw new InvalidOperationException($"The subject entity '{model.Entity.Name}' of process '{model.Name}' is not in the model.");
        }

        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;
        var read = PolicyEvaluator.Access(application, user.Roles, entity, PolicyAction.Read);
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The start lock comes first, before any entity table is touched, so a migrating activation
        // cannot deadlock with this retry. The release is read again under it.
        if (await StartLock.TakeSharedAsync(transaction, application.Manifest.Id, cancellationToken) != active.ReleaseId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var result = await ProcessRetryCommand.RetryAsync(
            connection,
            transaction,
            tenantId,
            application,
            model,
            entity,
            instanceId,
            read,
            user.Id,
            user.DisplayName,
            reason!,
            loggerFactory,
            cancellationToken);
        if (result.Outcome != ProcessRetryOutcome.Retried)
        {
            await transaction.RollbackAsync(cancellationToken);
            return result.Outcome switch
            {
                ProcessRetryOutcome.NotFound => InstanceNotFound(),
                ProcessRetryOutcome.PendingFilter => Grants.PendingFilter(),
                ProcessRetryOutcome.NotFailed => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The instance is not failed."),
                ProcessRetryOutcome.MigrationChanged => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A migration changed a column that the instance's release uses."),
                _ => ProcessStartEndpoints.AlreadyRunning(),
            };
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Json(
            new ProcessInstanceResponse(instanceId, model.Name, result.SubjectId!.Value, result.ReleaseId!.Value, ProcessStarts.Running));
    }

    /// <summary>
    /// Reads a body that is exactly <c>{ "reason": "&lt;text&gt;" }</c>. Every problem is reported
    /// in one pass, keyed by JSON Pointer into the body. A body that is not a JSON object is one
    /// problem on the whole body.
    /// </summary>
    private static (string? Reason, SortedDictionary<string, string[]>? Errors) ReadReason(byte[] utf8Body)
    {
        if (!Utf8.IsValid(utf8Body))
        {
            return Malformed();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Body);
        }
        catch (JsonException)
        {
            return Malformed();
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Malformed();
            }

            var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            string? reason = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                string name;
                try
                {
                    name = property.Name;
                }
                catch (InvalidOperationException)
                {
                    // A name with an unpaired surrogate escape cannot be read as text.
                    return Malformed();
                }

                var pointer = $"/{name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";
                if (!names.Add(name))
                {
                    errors.TryAdd(pointer, [DuplicateProperty]);
                }
                else if (name != ReasonProperty)
                {
                    errors.TryAdd(pointer, [UnknownProperty]);
                }
                else if (property.Value.ValueKind == JsonValueKind.String)
                {
                    try
                    {
                        reason = property.Value.GetString();
                    }
                    catch (InvalidOperationException)
                    {
                        return Malformed();
                    }
                }
            }

            if (!errors.ContainsKey("/" + ReasonProperty))
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    errors["/" + ReasonProperty] = ["Required."];
                }
                else if (reason.Length > MaxReasonLength)
                {
                    errors["/" + ReasonProperty] = [$"Must be at most {MaxReasonLength} characters."];
                }
            }

            return errors.Count > 0 ? (null, errors) : (reason, null);
        }
    }

    private static (string?, SortedDictionary<string, string[]>?) Malformed() =>
        (null, new SortedDictionary<string, string[]>(StringComparer.Ordinal) { [""] = [MalformedBody] });

    // Buffered, so a retry that runs again on a new release can read the body again.
    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        request.Body.Position = 0;
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, cancellationToken);
        request.Body.Position = 0;
        return body.ToArray();
    }

    private static IResult InstanceNotFound() => NotFound("No instance of this process exists with this id.");

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}
