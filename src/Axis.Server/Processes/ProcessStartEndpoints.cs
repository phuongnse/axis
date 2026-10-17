using System.Text.Json;
using System.Text.Unicode;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Core.Tenancy;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Records;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Microsoft.Extensions.Primitives;
using Npgsql;

namespace Axis.Server.Processes;

/// <summary>
/// The endpoint that starts a process instance for one subject record. The instance is pinned to
/// the release active when the request is resolved. A request is checked in order: session (401),
/// application and process (404), the user's <c>start</c> grant on the process (403), content type
/// (415), body (400), <c>Idempotency-Key</c> (400). Then one transaction claims the key's receipt,
/// locks the subject record, checks that the user may read it and reads it (400), checks the start
/// condition (400), inserts the instance (409) and its first work item, and appends the audit
/// record <c>process.started</c>, whose actor is the session user. A record the user may not read
/// gets the same 400 as one that does not exist, so the two cannot be told apart. A repeat with a
/// claimed key returns the stored response, or 422 for another record. A start that fails writes
/// nothing. The transaction takes the application's start lock shared first. When the active
/// release is no longer the one resolved, the start runs again from resolution on the new release,
/// and answers 503 after three attempts. The problem titles never contain text from the request.
/// </summary>
internal static class ProcessStartEndpoints
{
    private const string SubjectIdProperty = "subjectId";

    private const string SubjectIdPointer = "/" + SubjectIdProperty;

    internal const string IdempotencyKeyHeader = "Idempotency-Key";

    private const int MaxIdempotencyKeyLength = 255;

    public static void MapProcessEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/apps/{app}/processes/{process}/instances", StartAsync);

    // The body is read here rather than bound, so the content type is checked before any of the
    // body is read and every malformed body is the same validation problem.
    private static async Task<IResult> StartAsync(
        string app,
        string process,
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
            if (await StartOnceAsync(app, process, request, user, resolver, database, tenants, loggerFactory, cancellationToken) is { } result)
            {
                return result;
            }
        }

        return ReleaseRetry.Changed();
    }

    // Null when the active release changed while the start waited for the start lock. Nothing was written.
    private static async Task<IResult?> StartOnceAsync(
        string app,
        string process,
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

        if (!PolicyEvaluator.AllowsStart(application, user.Roles, model))
        {
            return Grants.Forbidden();
        }

        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        if (!TryReadSubjectId(await ReadBodyAsync(request, cancellationToken), out var subjectId))
        {
            return SubjectProblem("Must be the id of a record of the process's entity.");
        }

        if (!TryReadIdempotencyKey(request.Headers[IdempotencyKeyHeader], out var key))
        {
            return IdempotencyKeyProblem();
        }

        if (!application.TryGetEntity(model.Entity.Name, out var entity))
        {
            throw new InvalidOperationException($"The subject entity '{model.Entity.Name}' of process '{model.Name}' is not in the model.");
        }

        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;
        var instanceId = Guid.CreateVersion7();
        var response = new ProcessInstanceResponse(instanceId, model.Name, subjectId, active.ReleaseId, ProcessStarts.Running);
        var body = JsonSerializer.Serialize(response, JsonSerializerOptions.Web);

        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The start lock comes first, before any entity table is touched, so a migrating activation
        // cannot deadlock with this start. The release is read again under it.
        if (await StartLock.TakeSharedAsync(transaction, application.Manifest.Id, cancellationToken) != active.ReleaseId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        // The receipt is claimed first, so a repeat returns the stored response even when the
        // record has changed since, and a concurrent repeat waits on the receipt's key.
        if (key is not null
            && !await ProcessStarts.TryClaimReceiptAsync(transaction, application.Manifest.Id, model.Id, key, subjectId, instanceId, body, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            var receipt = await ProcessStarts.FindReceiptAsync(connection, application.Manifest.Id, model.Id, key, cancellationToken)
                ?? throw new InvalidOperationException("The receipt of a claimed Idempotency-Key could not be found.");
            return receipt.SubjectId == subjectId
                ? Results.Json(JsonSerializer.Deserialize<ProcessInstanceResponse>(receipt.Body, JsonSerializerOptions.Web), statusCode: receipt.StatusCode)
                : Results.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "This Idempotency-Key was used with a different subject record.");
        }

        // The lock makes the read below and the start condition see the latest version, and a
        // concurrent write of the record waits until this start commits or rolls back. A record
        // the user may not read does not exist for them.
        var read = PolicyEvaluator.Access(application, user.Roles, entity, PolicyAction.Read);
        if (!await RecordCommands.LockAsync(connection, entity, subjectId, cancellationToken)
            || !read.Granted
            || (read.Filters is { } filters
                && await RecordCommand.MatchAsync(connection, transaction, application, entity, subjectId, filters, user.Id, loggerFactory, cancellationToken)
                    is not { Found: true, Matched: true })
            || await RecordQueries.GetAsync(connection, application, entity, subjectId, cancellationToken) is not { } record)
        {
            await transaction.RollbackAsync(cancellationToken);
            return SubjectProblem("No record of the process's entity has this id.");
        }

        // The start condition reads the record with its pending computed fields computed by the active release.
        if (model.StartCondition is not null)
        {
            record = await PendingComputedReads.ApplyAsync(connection, transaction, application, entity, record, cancellationToken);
        }

        var started = await ProcessStarter.StartAsync(
            connection,
            transaction,
            tenantId,
            application,
            active.ReleaseId,
            model,
            entity,
            subjectId,
            record,
            instanceId,
            user.Id,
            user.DisplayName,
            details: null,
            cancellationToken);
        if (started.Outcome != ProcessStartOutcome.Started)
        {
            await transaction.RollbackAsync(cancellationToken);
            return started.Outcome == ProcessStartOutcome.ConditionFailed
                ? SubjectProblem(started.TextKey!)
                : AlreadyRunning();
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Json(response, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// Reads the <c>Idempotency-Key</c> header. No header gives a <see langword="null"/> key. A
    /// header must be one value of 1 to 255 characters.
    /// </summary>
    internal static bool TryReadIdempotencyKey(StringValues values, out string? key)
    {
        if (values.Count == 0)
        {
            key = null;
            return true;
        }

        key = values.Count == 1 ? values[0] : null;
        return key is { Length: > 0 and <= MaxIdempotencyKeyLength };
    }

    /// <summary>The 400 for an <c>Idempotency-Key</c> header that <see cref="TryReadIdempotencyKey"/> refuses.</summary>
    internal static IResult IdempotencyKeyProblem() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [IdempotencyKeyHeader] = [$"Must be 1 to {MaxIdempotencyKeyLength} characters."],
        });

    /// <summary>
    /// Reads a body that is exactly <c>{ "subjectId": "&lt;uuid&gt;" }</c>. Only the hyphenated
    /// form names a record.
    /// </summary>
    private static bool TryReadSubjectId(byte[] utf8Body, out Guid subjectId)
    {
        subjectId = Guid.Empty;
        if (!Utf8.IsValid(utf8Body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(utf8Body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var properties = root.EnumerateObject().ToList();
            return properties is [{ Value.ValueKind: JsonValueKind.String } property]
                && property.NameEquals(SubjectIdProperty)
                && property.Value.GetString() is { Length: 36 } text
                && Guid.TryParseExact(text, "D", out subjectId);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // A string with an unpaired surrogate escape cannot be read as text.
            return false;
        }
    }

    // Buffered, so a start that runs again on a new release can read the body again.
    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        request.Body.Position = 0;
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, cancellationToken);
        request.Body.Position = 0;
        return body.ToArray();
    }

    /// <summary>The problem of a start that found an instance already running or waiting for the record.</summary>
    internal static IResult AlreadyRunning() =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "A running or waiting instance of this process already exists for this record.");

    private static IResult SubjectProblem(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [SubjectIdPointer] = [message] });

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}

/// <summary>A started process instance, as a start returns it.</summary>
internal sealed record ProcessInstanceResponse(Guid Id, string Process, Guid SubjectId, Guid ReleaseId, string State);
