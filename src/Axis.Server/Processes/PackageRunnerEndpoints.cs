using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Packages;
using Axis.Core.Tenancy;
using Axis.Data.Records;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Server.Applications;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Tenancy;

namespace Axis.Server.Processes;

/// <summary>
/// The routes of a customer-hosted package runner, see <c>docs/reference/extension-packages.md#routes</c>. A request is checked in
/// order: the tenant of the host (<c>404</c>), the runner's token and row (<c>401</c>, see <see cref="PackageRunnerCaller"/>), the body
/// (<c>400</c>), then the releases (<c>409</c>).
/// <para>
/// <c>POST /api/package-runner/packages</c> checks each registered manifest with <see cref="RegisteredManifestCheck"/>. When every manifest
/// is equal to a pin, one statement adds the packages to the runner's stored list without duplicates and the answer is <c>204</c>. A manifest
/// that differs from a pin, or that no release in use pins as customer-hosted, answers <c>409</c> and stores nothing. The titles name the
/// package and the version, and no release or application. A runner that is revoked while the request runs answers <c>401</c> and stores
/// nothing.
/// </para>
/// <para>
/// <c>POST /api/package-runner/calls/take</c> reads <c>{ "packages": [{ "id", "version" }] }</c>, the packages the runner instance holds. In one
/// transaction, <see cref="PackageCallTakes"/> takes the oldest waiting call to one of them that the runner also registered, and the runner's last
/// call time is set. The answer is <c>200</c> with the lease, the package, the version, the operation, the operation identity, the input and the end of
/// the lease, and <c>204</c> when no call waits. A runner that is revoked while the request runs answers <c>401</c> and takes nothing. Like a
/// registration, a take is never refused over the <c>storage</c> quota, because it is running work.
/// </para>
/// <para>
/// <c>POST /api/package-runner/leases/{leaseId}/result</c> reads <c>{ "outcome": "succeeded", "output": {...} }</c>, <c>{ "outcome": "retryable" }</c> or
/// <c>{ "outcome": "failed", "error": "..." }</c> with a non-empty <c>error</c>, and any other body answers <c>400</c>. In one transaction,
/// <see cref="PackageCallResults"/> records the call the same way the worker does after a run, and the answer is <c>204</c>, also when the output does
/// not fit, when the runner posted <c>failed</c> or a <c>retryable</c> result has no attempt left and the call and its instance fail, and when a
/// <c>retryable</c> result sets the call back to <c>pending</c>. A lease
/// that is unknown, or that another runner holds, answers <c>404</c>. A lease whose call is no longer <c>sending</c>, whose lease has ended or whose
/// instance no longer waits on the step answers <c>409</c> and changes nothing. A result is never refused over the <c>storage</c> quota, because it
/// is running work. A runner that is revoked while the request runs is not checked again, so its next request answers <c>401</c>.
/// </para>
/// </summary>
internal static class PackageRunnerEndpoints
{
    public static void MapPackageRunnerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/package-runner/packages", RegisterAsync);
        endpoints.MapPost("/api/package-runner/calls/take", TakeAsync);
        endpoints.MapPost("/api/package-runner/leases/{leaseId}/result", ResultAsync);
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context, TenantDatabase database, PackageCatalog packages, CancellationToken cancellationToken)
    {
        using var body = await ReadBodyAsync(context.Request, cancellationToken);
        if (body is null || ReadManifests(body.RootElement) is not { } manifests)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The body needs a list of packages, each with a manifest that has an id and a version.");
        }

        var runnerId = Guid.Parse(EndpointCaller.Read(context.User)!.Id);
        var connection = await database.GetConnectionAsync(cancellationToken);

        // The check throws when a release in use no longer compiles. That is a server fault, so it is not caught here.
        foreach (var (id, version, manifest) in manifests)
        {
            switch (await RegisteredManifestCheck.CheckAsync(connection, manifest, packages, cancellationToken))
            {
                case RegisteredManifestOutcome.Different:
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: $"The manifest of package '{id}' version {version} differs from the one a release in use pins.");
                case RegisteredManifestOutcome.NotPinned:
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: $"No release in use pins package '{id}' version {version} as customer-hosted.");
            }
        }

        var added = manifests.Select(manifest => new PackageRunnerPackage(manifest.Id, manifest.Version)).Distinct().ToList();
        return await PackageRunners.AddPackagesAsync(connection, runnerId, added, cancellationToken)
            ? Results.NoContent()
            : Grants.PackageRunnerTokenRequired(context);
    }

    private static async Task<IResult> TakeAsync(
        HttpContext context,
        TenantDatabase database,
        PackageCatalog packages,
        ActiveApplicationResolver resolver,
        CancellationToken cancellationToken)
    {
        using var body = await ReadBodyAsync(context.Request, cancellationToken);
        if (body is null || ReadPackages(body.RootElement) is not { } held)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The body needs a list of packages, each with an id and a version.");
        }

        var runnerId = Guid.Parse(EndpointCaller.Read(context.User)!.Id);
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The write limit comes first, so it also bounds the wait for a row that another take has locked.
        await database.LimitWriteAsync(transaction, cancellationToken);
        var take = await PackageCallTakes.TakeAsync(transaction, runnerId, held, packages, resolver.GetReleaseModelAsync, cancellationToken);
        if (take is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Results.NoContent();
        }

        if (!await PackageRunners.MarkCalledAsync(transaction, runnerId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Grants.PackageRunnerTokenRequired(context);
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new TakeResponse(take.LeaseId, take.Package, take.Version, take.Operation, take.OperationId, take.Input, take.LeaseUntil));
    }

    private static async Task<IResult> ResultAsync(
        HttpContext context,
        string leaseId,
        TenantDatabase database,
        PackageCatalog packages,
        ActiveApplicationResolver resolver,
        SensitiveValues sensitive,
        ITenantContextAccessor tenants,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var body = await ReadBodyAsync(context.Request, cancellationToken);
        if (body is null || ReadResult(body.RootElement) is not { } result)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The body needs the outcome succeeded with an output object, retryable, or failed with an error.");
        }

        if (!Guid.TryParseExact(leaseId, "D", out var lease))
        {
            return LeaseNotKnown();
        }

        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;
        var runnerId = Guid.Parse(EndpointCaller.Read(context.User)!.Id);
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The write limit comes first, so it also bounds the wait for a call that another request has locked.
        await database.LimitWriteAsync(transaction, cancellationToken);
        var outcome = await PackageCallResults.RecordAsync(
            transaction,
            tenantId,
            runnerId,
            lease,
            result,
            packages,
            resolver.GetReleaseModelAsync,
            new RecordCipher(sensitive, tenantId),
            loggerFactory,
            cancellationToken);
        switch (outcome)
        {
            case PackageCallResultOutcome.UnknownLease:
                await transaction.RollbackAsync(cancellationToken);
                return LeaseNotKnown();
            case PackageCallResultOutcome.Conflict:
                await transaction.RollbackAsync(cancellationToken);
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The lease has ended or its call already has a result.");
            default:
                await transaction.CommitAsync(cancellationToken);
                return Results.NoContent();
        }
    }

    private static IResult LeaseNotKnown() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The lease is not known.");

    /// <summary>The parsed body, or null when it is not JSON.</summary>
    private static async Task<JsonDocument?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The manifests of <c>{ "packages": [{ "manifest": {...} }] }</c>, or null when the shape is wrong: the list must not be empty, and
    /// each manifest must be an object with a non-empty <c>id</c> and <c>version</c>, which are strings.
    /// </summary>
    private static List<(string Id, string Version, JsonElement Manifest)>? ReadManifests(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("packages", out var list)
            || list.ValueKind != JsonValueKind.Array
            || list.GetArrayLength() == 0)
        {
            return null;
        }

        var manifests = new List<(string, string, JsonElement)>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("manifest", out var manifest)
                || !TryReadText(manifest, "id", out var id)
                || !TryReadText(manifest, "version", out var version))
            {
                return null;
            }

            manifests.Add((id, version, manifest));
        }

        return manifests;
    }

    /// <summary>
    /// The packages of <c>{ "packages": [{ "id", "version" }] }</c>, or null when the shape is wrong: the list must not be empty, and each item
    /// must be an object with a non-empty <c>id</c> and <c>version</c>, which are strings.
    /// </summary>
    private static List<PackageRunnerPackage>? ReadPackages(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("packages", out var list)
            || list.ValueKind != JsonValueKind.Array
            || list.GetArrayLength() == 0)
        {
            return null;
        }

        var packages = new List<PackageRunnerPackage>();
        foreach (var item in list.EnumerateArray())
        {
            if (!TryReadText(item, "id", out var id) || !TryReadText(item, "version", out var version))
            {
                return null;
            }

            packages.Add(new PackageRunnerPackage(id, version));
        }

        return packages;
    }

    /// <summary>
    /// The result of <c>{ "outcome": "succeeded", "output": {...} }</c>, <c>{ "outcome": "retryable" }</c> or
    /// <c>{ "outcome": "failed", "error": "..." }</c>, or null when the shape is wrong: the outcome must be exactly one of the three, the output
    /// of <c>succeeded</c> an object and the error of <c>failed</c> a non-empty string. Other properties are ignored.
    /// </summary>
    private static PackageCallResult? ReadResult(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("outcome", out var outcome)
            || outcome.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        switch (outcome.GetString())
        {
            case "succeeded":
                return root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Object
                    ? new PackageCallResult.Succeeded(JsonNode.Parse(output.GetRawText())!.AsObject())
                    : null;
            case "retryable":
                return new PackageCallResult.Retryable();
            case "failed":
                return TryReadText(root, "error", out var error) ? new PackageCallResult.Failed(error) : null;
            default:
                return null;
        }
    }

    private static bool TryReadText(JsonElement manifest, string property, out string value)
    {
        value = string.Empty;
        if (manifest.ValueKind == JsonValueKind.Object
            && manifest.TryGetProperty(property, out var element)
            && element.ValueKind == JsonValueKind.String
            && element.GetString() is { Length: > 0 } text)
        {
            value = text;
            return true;
        }

        return false;
    }
}

/// <summary>The answer to a take: the lease and the call, with no binding value in the input.</summary>
internal sealed record TakeResponse(
    Guid LeaseId,
    string Package,
    string Version,
    string Operation,
    Guid OperationId,
    JsonObject Input,
    DateTimeOffset LeaseUntil);
