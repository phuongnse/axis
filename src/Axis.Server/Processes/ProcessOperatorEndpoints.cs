using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Core.Tenancy;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Server.Applications;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Records;
using Axis.Server.Tenancy;
using Axis.Server.Users;

namespace Axis.Server.Processes;

/// <summary>
/// The endpoints for a user or a service account that may operate on the instances of a process. The caller is
/// the session user or, on these routes, a service account with a bearer token, and its id and name are the
/// actor of the records a command writes. A cancel at
/// <c>POST /api/apps/{app}/processes/{process}/instances/{id}/cancel</c> ends a <c>running</c> or
/// <c>waiting</c> instance as <c>cancelled</c> and answers 200 with the instance. A request is
/// checked in order: session or service account bearer token (401), application and process (404), the user's <c>cancel</c> grant
/// on the process (403), content type (415), then the body (400), which is exactly
/// <c>{ "reason": "&lt;text&gt;" }</c> with a reason of 1 to 500 characters. An <c>id</c> that is
/// not a hyphenated UUID is a 404, as is an instance of another process or one whose subject
/// record the user may not read. An instance of a process without a subject has no record, so only the grant
/// applies to it. Then one transaction runs <see cref="ProcessCancelCommand"/>,
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
/// for an instance that is not failed, 409 when another instance of the process is running or waiting for the record, or of a process without a subject at all, and
/// 409 when an applied migration changed a column that the instance's pinned release uses.
/// When the active release is no longer the one resolved, the retry runs again from resolution on
/// the new release, and answers 503 after three attempts. A retry that is refused writes nothing.
/// A resolve at <c>POST /api/apps/{app}/processes/{process}/instances/{id}/calls/{callId}/resolve</c>
/// settles an <c>unknown</c> connector or package call of an instance that is <c>waiting</c> on the call's step, as
/// <c>succeeded</c> with its <c>output</c> or as <c>failed</c>. It is checked in the same order with the user's
/// <c>resolve</c> grant, and its body is a <c>reason</c>, an <c>outcome</c> and, for <c>succeeded</c>, an
/// <c>output</c> object. Then one transaction takes the start lock shared, as a retry does, and runs
/// <see cref="CallResolveCommand"/>. It answers 404 for a <c>callId</c> that is not a connector or package call of the
/// instance, 409 for a call that is not unknown, 409 for an instance that is not waiting on the call's step, the same
/// 409 for a <c>read</c> filter on a field that is being recomputed, and 400 for an output that does not fit the
/// operation's declared outputs or a write the record refuses. A resolve that is refused writes nothing.
/// The problem titles never contain text from the request.
/// </summary>
internal static class ProcessOperatorEndpoints
{
    internal const string ReasonProperty = "reason";

    private const string OutcomeProperty = "outcome";

    private const string OutputProperty = "output";

    private const int MaxReasonLength = 500;

    // The record API's messages for the same problems.
    private const string MalformedBody = "Must be a JSON object.";

    private const string UnknownProperty = "Unknown property.";

    private const string DuplicateProperty = "Duplicate property.";

    private const string MustBeObject = "Must be an object.";

    public static void MapProcessOperatorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/apps/{app}/processes/{process}/instances/{id}/cancel", CancelAsync)
            .WithMetadata(ServiceAccountRoute.Instance);
        endpoints.MapPost("/api/apps/{app}/processes/{process}/instances/{id}/retry", RetryAsync)
            .WithMetadata(ServiceAccountRoute.Instance);
        endpoints.MapPost("/api/apps/{app}/processes/{process}/instances/{id}/calls/{callId}/resolve", ResolveAsync)
            .WithMetadata(ServiceAccountRoute.Instance);
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
            return Grants.SignInRequired(request.HttpContext);
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

        if (!PolicyEvaluator.AllowsCancel(application, user.RolesIn(application), model))
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

        var (entity, read) = SubjectAccess(application, user, model);
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
            new ProcessInstanceResponse(instanceId, model.Name, result.SubjectId, result.ReleaseId!.Value, ProcessStarts.Cancelled));
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
            return Grants.SignInRequired(request.HttpContext);
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

        if (!PolicyEvaluator.AllowsRetry(application, user.RolesIn(application), model))
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

        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;
        var (entity, read) = SubjectAccess(application, user, model);
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
                _ => model.Entity is null ? ProcessStartEndpoints.AlreadyRunningWithoutSubject() : ProcessStartEndpoints.AlreadyRunning(),
            };
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Json(
            new ProcessInstanceResponse(instanceId, model.Name, result.SubjectId, result.ReleaseId!.Value, ProcessStarts.Running));
    }

    private static async Task<IResult> ResolveAsync(
        string app,
        string process,
        string id,
        string callId,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        PackageCatalog packages,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired(request.HttpContext);
        }

        // A release change under the start lock sends the request back to resolution, so every check runs on the new release.
        for (var attempt = 0; attempt < ReleaseRetry.MaxAttempts; attempt++)
        {
            if (await ResolveOnceAsync(app, process, id, callId, request, user, resolver, database, tenants, packages, loggerFactory, cancellationToken) is { } result)
            {
                return result;
            }
        }

        return ReleaseRetry.Changed();
    }

    // Null when the active release changed while the resolve waited for the start lock. Nothing was written.
    private static async Task<IResult?> ResolveOnceAsync(
        string app,
        string process,
        string id,
        string callId,
        HttpRequest request,
        CurrentUserResponse user,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        PackageCatalog packages,
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

        if (!PolicyEvaluator.AllowsResolve(application, user.RolesIn(application), model))
        {
            return Grants.Forbidden();
        }

        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var (body, errors) = ReadResolve(await ReadBodyAsync(request, cancellationToken));
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var instanceId))
        {
            return InstanceNotFound();
        }

        if (callId is not { Length: 36 } || !Guid.TryParseExact(callId, "D", out var operationId))
        {
            return CallNotFound();
        }

        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;
        var (entity, read) = SubjectAccess(application, user, model);
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The start lock comes first, before any entity table is touched, because a succeeded resolve writes the
        // record and can start instances. The release is read again under it.
        if (await StartLock.TakeSharedAsync(transaction, application.Manifest.Id, cancellationToken) != active.ReleaseId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var result = await CallResolveCommand.ResolveAsync(
            connection,
            transaction,
            tenantId,
            application,
            active.ReleaseId,
            model,
            entity,
            instanceId,
            operationId,
            read,
            user.Id,
            body!.Reason,
            body.Succeeded,
            body.Output,
            resolver.GetReleaseModelAsync,
            packages,
            loggerFactory,
            cancellationToken);
        if (result.Outcome != CallResolveOutcome.Resolved)
        {
            await transaction.RollbackAsync(cancellationToken);
            return result.Outcome switch
            {
                CallResolveOutcome.NotFound => InstanceNotFound(),
                CallResolveOutcome.CallNotFound => CallNotFound(),
                CallResolveOutcome.PendingFilter => Grants.PendingFilter(),
                CallResolveOutcome.NotUnknown => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The call's outcome is not unknown."),
                CallResolveOutcome.NotWaiting => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The instance is not waiting on the call's step."),
                _ => Results.ValidationProblem(result.Errors!),
            };
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Json(
            new ProcessInstanceResponse(instanceId, model.Name, result.SubjectId, result.ReleaseId!.Value, result.State!));
    }

    /// <summary>
    /// Reads a body that is exactly <c>{ "reason": "&lt;text&gt;" }</c>. Every problem is reported
    /// in one pass, keyed by JSON Pointer into the body. A body that is not a JSON object is one
    /// problem on the whole body.
    /// </summary>
    internal static (string? Reason, SortedDictionary<string, string[]>? Errors) ReadReason(byte[] utf8Body) =>
        ReadObject(utf8Body, [ReasonProperty], (properties, errors) => ReadReasonText(properties, errors));

    /// <summary>
    /// Reads a resolve body: a <c>reason</c>, an <c>outcome</c> of <c>succeeded</c> or <c>failed</c> and,
    /// with <c>succeeded</c>, an optional <c>output</c> object. Every problem is reported in one pass, keyed
    /// by JSON Pointer into the body. Whether the output fits the operation is checked later.
    /// </summary>
    private static (ResolveBody? Body, SortedDictionary<string, string[]>? Errors) ReadResolve(byte[] utf8Body) =>
        ReadObject(
            utf8Body,
            [ReasonProperty, OutcomeProperty, OutputProperty],
            (properties, errors) =>
            {
                var reason = ReadReasonText(properties, errors);
                bool? succeeded = null;
                if (!errors.ContainsKey("/" + OutcomeProperty))
                {
                    if (!properties.TryGetValue(OutcomeProperty, out var outcome) || outcome.ValueKind == JsonValueKind.Null)
                    {
                        errors["/" + OutcomeProperty] = ["Required."];
                    }
                    else
                    {
                        succeeded = outcome.ValueKind == JsonValueKind.String ? outcome.GetString() switch
                        {
                            "succeeded" => true,
                            "failed" => false,
                            _ => null,
                        }
                        : null;
                        if (succeeded is null)
                        {
                            errors["/" + OutcomeProperty] = ["Must be succeeded or failed."];
                        }
                    }
                }

                JsonObject? output = null;
                if (!errors.ContainsKey("/" + OutputProperty) && properties.TryGetValue(OutputProperty, out var element))
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        errors["/" + OutputProperty] = [MustBeObject];
                    }
                    else if (succeeded == false)
                    {
                        errors["/" + OutputProperty] = ["Not allowed when the outcome is failed."];
                    }
                    else
                    {
                        output = ReadOutput(element, errors);
                    }
                }

                return reason is not null && succeeded is { } settled ? new ResolveBody(reason, settled, output) : null;
            });

    // The names are checked for duplicates here, and each value is kept as parsed. The type of each is checked later.
    private static JsonObject? ReadOutput(JsonElement element, SortedDictionary<string, string[]> errors)
    {
        var output = new JsonObject();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            string name;
            try
            {
                name = property.Name;
            }
            catch (InvalidOperationException)
            {
                // A name with an unpaired surrogate escape cannot be read as text.
                errors["/" + OutputProperty] = [MalformedBody];
                return null;
            }

            var pointer = Pointer(OutputProperty, name);
            if (!names.Add(name))
            {
                errors.TryAdd(pointer, [DuplicateProperty]);
                continue;
            }

            try
            {
                output[name] = JsonNode.Parse(property.Value.GetRawText());
            }
            catch (ArgumentException)
            {
                // A nested object repeats a name.
                errors[pointer] = [MalformedBody];
            }
        }

        return output;
    }

    internal static string? ReadReasonText(Dictionary<string, JsonElement> properties, SortedDictionary<string, string[]> errors)
    {
        if (errors.ContainsKey("/" + ReasonProperty))
        {
            return null;
        }

        var reason = properties.TryGetValue(ReasonProperty, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        if (string.IsNullOrWhiteSpace(reason))
        {
            errors["/" + ReasonProperty] = ["Required."];
            return null;
        }

        if (reason.Length > MaxReasonLength)
        {
            errors["/" + ReasonProperty] = [$"Must be at most {MaxReasonLength} characters."];
            return null;
        }

        return reason;
    }

    /// <summary>
    /// Parses <paramref name="utf8Body"/> as a JSON object whose properties are among <paramref name="allowed"/>,
    /// and reports an unknown or a repeated property. <paramref name="read"/> reads the properties that were
    /// accepted and adds its own errors. A body that is not a JSON object is one problem on the whole body.
    /// </summary>
    internal static (T? Value, SortedDictionary<string, string[]>? Errors) ReadObject<T>(
        byte[] utf8Body,
        string[] allowed,
        Func<Dictionary<string, JsonElement>, SortedDictionary<string, string[]>, T?> read)
        where T : class
    {
        if (!Utf8.IsValid(utf8Body))
        {
            return Malformed<T>();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Body);
        }
        catch (JsonException)
        {
            return Malformed<T>();
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Malformed<T>();
            }

            var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                string name;
                try
                {
                    name = property.Name;
                    if (property.Value.ValueKind == JsonValueKind.String && allowed.Contains(name))
                    {
                        _ = property.Value.GetString();
                    }
                }
                catch (InvalidOperationException)
                {
                    // A name or a text with an unpaired surrogate escape cannot be read as text.
                    return Malformed<T>();
                }

                if (!names.Add(name))
                {
                    errors.TryAdd(Pointer(name), [DuplicateProperty]);
                }
                else if (!allowed.Contains(name))
                {
                    errors.TryAdd(Pointer(name), [UnknownProperty]);
                }
                else
                {
                    properties[name] = property.Value;
                }
            }

            var value = read(properties, errors);
            return errors.Count > 0 ? (null, errors) : (value, null);
        }
    }

    private static string Pointer(params string[] names) =>
        string.Concat(names.Select(name => $"/{name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}"));

    private static (T?, SortedDictionary<string, string[]>?) Malformed<T>() where T : class =>
        (null, new SortedDictionary<string, string[]>(StringComparer.Ordinal) { [""] = [MalformedBody] });

    // Buffered, so a retry that runs again on a new release can read the body again.
    internal static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        request.Body.Position = 0;
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, cancellationToken);
        request.Body.Position = 0;
        return body.ToArray();
    }

    // A process without a subject has no record to read, so it has no entity and no read access. The command then
    // checks only the grant that the endpoint has checked.
    private static (EntityModel? Entity, RecordAccess? Read) SubjectAccess(ApplicationModel application, CurrentUserResponse user, ProcessModel model)
    {
        if (model.Entity is not { } subjectEntity)
        {
            return (null, null);
        }

        if (!application.TryGetEntity(subjectEntity.Name, out var entity))
        {
            throw new InvalidOperationException($"The subject entity '{subjectEntity.Name}' of process '{model.Name}' is not in the model.");
        }

        return (entity, PolicyEvaluator.Access(application, user.RolesIn(application), entity, PolicyAction.Read));
    }

    private static IResult InstanceNotFound() => NotFound("No instance of this process exists with this id.");

    private static IResult CallNotFound() => NotFound("No call of this instance exists with this id.");

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}

/// <summary>The body of a resolve: the reason, whether the call succeeded, and the output of a succeeded call.</summary>
internal sealed record ResolveBody(string Reason, bool Succeeded, JsonObject? Output);
