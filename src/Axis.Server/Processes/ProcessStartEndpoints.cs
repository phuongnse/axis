using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using Axis.Configuration.Model;
using Axis.Core.Tenancy;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Microsoft.Extensions.Primitives;
using Npgsql;

namespace Axis.Server.Processes;

/// <summary>
/// The endpoint that starts a process instance for one subject record. The instance is pinned to
/// the release active when the request is resolved. A request is checked in order: application
/// and process (404), content type (415), body (400), <c>Idempotency-Key</c> (400). Then one
/// transaction claims the key's receipt, reads the subject record (400), checks the start
/// condition (400), inserts the instance (409) and its first work item, and appends the audit
/// record <c>process.started</c>. A repeat with a claimed key returns the stored response, or 422
/// for another record. A start that fails writes nothing. The problem titles never contain text
/// from the request.
/// </summary>
internal static class ProcessStartEndpoints
{
    private const string SubjectIdProperty = "subjectId";

    private const string SubjectIdPointer = "/" + SubjectIdProperty;

    private const string IdempotencyKeyHeader = "Idempotency-Key";

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
        TestUserDirectory directory,
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
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [IdempotencyKeyHeader] = [$"Must be 1 to {MaxIdempotencyKeyLength} characters."],
            });
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

        if (await RecordQueries.GetAsync(connection, application, entity, subjectId, cancellationToken) is not { } record)
        {
            await transaction.RollbackAsync(cancellationToken);
            return SubjectProblem("No record of the process's entity has this id.");
        }

        // Only true passes: false, null and a run-time error all fail the start. now() is this
        // transaction's start time.
        if (model.StartCondition is { } condition)
        {
            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            var result = await RecordExpressions.EvaluateAsync(connection, application, entity, record, condition.Expression, now, cancellationToken);
            if (result.Value is not true)
            {
                await transaction.RollbackAsync(cancellationToken);
                return SubjectProblem(condition.Message.TextKey);
            }
        }

        var instance = new ProcessInstanceRow
        {
            Id = instanceId,
            ApplicationId = application.Manifest.Id,
            ProcessId = model.Id,
            SubjectEntityId = entity.Id,
            SubjectId = subjectId,
            ReleaseId = active.ReleaseId,
            State = ProcessStarts.Running,
            Revision = 1,
            Step = model.Start,
        };
        if (!await ProcessStarts.TryInsertInstanceAsync(transaction, instance, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A running or waiting instance of this process already exists for this record.");
        }

        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            tenantId,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            processInstanceId: instanceId);
        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                directory.FindSignedIn(request.HttpContext.User) ?? AuditActors.Anonymous,
                AuditActions.ProcessStarted,
                application.Manifest.Id,
                entity.Id,
                subjectId,
                instanceId,
                new JsonObject { ["processId"] = model.Id, ["releaseId"] = active.ReleaseId }),
            cancellationToken);
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

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, cancellationToken);
        return body.ToArray();
    }

    private static IResult SubjectProblem(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [SubjectIdPointer] = [message] });

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}

/// <summary>A started process instance, as a start returns it.</summary>
internal sealed record ProcessInstanceResponse(Guid Id, string Process, Guid SubjectId, Guid ReleaseId, string State);
