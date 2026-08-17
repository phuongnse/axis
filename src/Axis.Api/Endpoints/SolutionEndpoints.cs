using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Api.Middleware;
using Axis.Identity.Contracts;
using Axis.Solutions.Application;
using Axis.Solutions.Contracts;
using Axis.Solutions.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class SolutionEndpoints
{
    private const int MaximumPackageBytes = 10 * 1024 * 1024;
    private const string PackageMediaType = "application/vnd.dsse.envelope.v1+json";

    public static IEndpointRouteBuilder MapSolutionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/solutions")
            .RequireAuthorization(AxisApiServiceExtensions.WorkspaceAccessPolicy)
            .WithTags("Solutions");

        group.MapPost("/versions", Publish)
            .WithName("PublishSolutionVersion")
            .WithSummary("Publish one trusted immutable signed solution package")
            .Accepts<byte[]>(PackageMediaType)
            .Produces<PublishSolutionResponse>(StatusCodes.Status201Created)
            .Produces<PublishSolutionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/versions", ListVersions)
            .WithName("ListSolutionVersions")
            .WithSummary("List safe published solution version status")
            .Produces<IReadOnlyList<SolutionVersionSummaryDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/versions/{solutionVersionId:guid}", GetVersionStatus)
            .WithName("GetSolutionVersionStatus")
            .WithSummary("Get safe status for one published solution version")
            .Produces<SolutionVersionSummaryDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/versions/{solutionVersionId:guid}/installations", Install)
            .WithName("InstallSolutionVersion")
            .WithSummary("Begin installation of an exact trusted solution version")
            .Produces<InstallSolutionResponse>(StatusCodes.Status201Created)
            .Produces<InstallSolutionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithMetadata(RequiredIdempotencyKeyMetadata.Instance);

        group.MapGet("/installations", ListInstallations)
            .WithName("ListSolutionInstallations")
            .WithSummary("List safe solution installation status for the current Workspace")
            .Produces<IReadOnlyList<SolutionInstallationStatusDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/operations/{operationId:guid}", GetOperationStatus)
            .WithName("GetSolutionOperationStatus")
            .WithSummary("Get safe status for one current-Workspace solution operation")
            .Produces<SolutionOperationStatusDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/operations/{operationId:guid}/resume", Resume)
            .WithName("ResumeSolutionInstallation")
            .WithSummary("Resume one recoverable current-Workspace solution operation")
            .Produces<SolutionOperationStatusDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> Publish(
        HttpRequest request,
        HttpContext httpContext,
        CurrentUser currentUser,
        ICurrentSubject currentSubject,
        SolutionOrchestrator orchestrator,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                request.ContentType?.Split(';', 2)[0],
                PackageMediaType,
                StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                "solutions.package.content_type_invalid",
                StatusCodes.Status415UnsupportedMediaType,
                $"The solution package must be sent as {PackageMediaType}.");
        }

        if (request.ContentLength is > MaximumPackageBytes)
        {
            return Problem(
                "solutions.package.too_large",
                StatusCodes.Status413PayloadTooLarge,
                "The solution package exceeds the 10 MiB limit.");
        }

        byte[]? envelope = await ReadPackageAsync(request.Body, cancellationToken);
        if (envelope is null)
        {
            return Problem(
                "solutions.package.too_large",
                StatusCodes.Status413PayloadTooLarge,
                "The solution package exceeds the 10 MiB limit.");
        }
        if (envelope.Length == 0)
        {
            return Problem(
                "solutions.package.empty",
                StatusCodes.Status400BadRequest,
                "A solution package is required.");
        }

        try
        {
            PublishSolutionResult result = await orchestrator.PublishAsync(
                new PublishSolutionRequest(
                    Actor(currentUser, currentSubject, httpContext),
                    envelope,
                    clock.GetUtcNow()),
                cancellationToken);
            PublishSolutionResponse response = new(result.Version, result.IsRetry);
            return Results.Json(
                response,
                statusCode: result.IsRetry
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status201Created);
        }
        catch (SolutionPackageException exception)
        {
            return ToProblem(exception.ProblemCode);
        }
        catch (SolutionPersistenceException exception)
        {
            return ToProblem(exception.ProblemCode);
        }
        catch (SolutionAdapterException exception)
        {
            return ToProblem(exception.ProblemCode);
        }
    }

    private static async Task<IResult> Install(
        Guid solutionVersionId,
        [FromHeader(Name = RequiredIdempotencyKeyMetadata.HeaderName), Required]
        string? idempotencyKey,
        HttpContext context,
        CurrentUser currentUser,
        ICurrentSubject currentSubject,
        SolutionOrchestrator orchestrator,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        string normalizedIdempotencyKey = idempotencyKey?.Trim() ?? string.Empty;
        if (solutionVersionId == Guid.Empty
            || normalizedIdempotencyKey.Length is 0 or > 200)
        {
            return Problem(
                "solutions.install.invalid_request",
                StatusCodes.Status400BadRequest,
                "A valid solution version and Idempotency-Key are required.");
        }

        try
        {
            InstallSolutionResult result = await orchestrator.BeginInstallAsync(
                new InstallSolutionRequest(
                    Actor(currentUser, currentSubject, context),
                    currentUser.WorkspaceId,
                    solutionVersionId,
                    normalizedIdempotencyKey,
                    InstallRequestHash(currentUser.WorkspaceId, solutionVersionId),
                    clock.GetUtcNow()),
                cancellationToken);
            InstallSolutionResponse response = new(result.Operation, result.IsRetry);
            return result.IsRetry
                ? Results.Ok(response)
                : Results.Created($"/api/solutions/operations/{result.Operation.Id}", response);
        }
        catch (SolutionPackageException exception)
        {
            return ToProblem(exception.ProblemCode);
        }
        catch (SolutionPersistenceException exception)
        {
            return ToProblem(exception.ProblemCode);
        }
        catch (InvalidOperationException)
        {
            return ToProblem("solutions.install.operation_conflict");
        }
        catch (SolutionAdapterException exception)
        {
            return ToProblem(exception.ProblemCode);
        }
    }

    private static async Task<IResult> ListVersions(
        HttpContext context,
        CurrentUser currentUser,
        ICurrentSubject currentSubject,
        SolutionOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await orchestrator.ListVersionStatusAsync(
                Actor(currentUser, currentSubject, context),
                cancellationToken));
        }
        catch (SolutionPackageException exception)
        {
            return ToProblem(exception.ProblemCode);
        }
    }

    private static async Task<IResult> GetVersionStatus(
        Guid solutionVersionId,
        HttpContext context,
        CurrentUser currentUser,
        ICurrentSubject currentSubject,
        SolutionOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await orchestrator.GetVersionStatusAsync(
                Actor(currentUser, currentSubject, context),
                solutionVersionId,
                cancellationToken));
        }
        catch (SolutionPackageException exception)
        {
            return ToProblem(exception.ProblemCode);
        }
    }

    private static async Task<IResult> ListInstallations(
        HttpContext context,
        CurrentUser currentUser,
        ICurrentSubject currentSubject,
        SolutionOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<SolutionInstallationStatusDto> result =
                await orchestrator.ListInstallationStatusAsync(
                    Actor(currentUser, currentSubject, context),
                    cancellationToken);
            return Results.Ok(result);
        }
        catch (SolutionPackageException exception)
        {
            return ToProblem(exception.ProblemCode);
        }
    }

    private static async Task<IResult> GetOperationStatus(
        Guid operationId,
        HttpContext context,
        CurrentUser currentUser,
        ICurrentSubject currentSubject,
        SolutionOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await orchestrator.GetOperationStatusAsync(
                Actor(currentUser, currentSubject, context),
                operationId,
                cancellationToken));
        }
        catch (SolutionPackageException exception)
        {
            return ToProblem(exception.ProblemCode);
        }
    }

    private static async Task<IResult> Resume(
        Guid operationId,
        HttpContext context,
        CurrentUser currentUser,
        ICurrentSubject currentSubject,
        SolutionOrchestrator orchestrator,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await orchestrator.ResumeAsync(
                Actor(currentUser, currentSubject, context),
                operationId,
                clock.GetUtcNow(),
                cancellationToken));
        }
        catch (SolutionPackageException exception)
        {
            return ToProblem(exception.ProblemCode);
        }
        catch (SolutionPersistenceException exception)
        {
            return ToProblem(exception.ProblemCode);
        }
        catch (InvalidOperationException)
        {
            return ToProblem("solutions.install.operation_conflict");
        }
    }

    private static async Task<byte[]?> ReadPackageAsync(
        Stream body,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using MemoryStream destination = new();
            while (true)
            {
                int read = await body.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    return destination.ToArray();
                if (destination.Length + read > MaximumPackageBytes)
                    return null;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static SolutionActor Actor(
        CurrentUser currentUser,
        ICurrentSubject currentSubject,
        HttpContext context) =>
        new(
            currentSubject.Subject.Id,
            currentUser.WorkspaceId,
            CorrelationId(context),
            currentSubject.Subject.Kind == SubjectKind.Service
                ? SolutionSubjectKind.Service
                : SolutionSubjectKind.Human,
            currentSubject.DisplayName);

    private static string CorrelationId(HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out object? value)
            && value is string correlationId
                ? correlationId
                : context.TraceIdentifier;

    private static string InstallRequestHash(Guid workspaceId, Guid solutionVersionId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{workspaceId:D}\n{solutionVersionId:D}"))).ToLowerInvariant();

    private static IResult ToProblem(string problemCode)
    {
        int statusCode = problemCode switch
        {
            "solutions.authorization.denied" or
            "solutions.authorization.workspace_mismatch" or
            "solutions.version.not_found" or
            "solutions.operation.not_found" or
            "solutions.installation.not_found" => StatusCodes.Status404NotFound,
            "solutions.install.idempotency_conflict" or
            "solutions.version.conflict" or
            "solutions.install.already_exists" or
            "solutions.package.publisher_untrusted" or
            "solutions.package.axis_openapi_mismatch" or
            "solutions.install.readback_mismatch" => StatusCodes.Status409Conflict,
            "authorization.policy_invalid" or
            "businessObjects.definition_component_invalid" or
            "rules.binding_component_invalid" => StatusCodes.Status422UnprocessableEntity,
            _ when problemCode.StartsWith("authorization.", StringComparison.Ordinal) =>
                StatusCodes.Status409Conflict,
            _ when problemCode.StartsWith("businessObjects.", StringComparison.Ordinal) =>
                StatusCodes.Status409Conflict,
            _ when problemCode.StartsWith("rules.", StringComparison.Ordinal) =>
                StatusCodes.Status409Conflict,
            "solutions.install.adapter_unavailable" or
            "solutions.install.readback_unconfirmed" => StatusCodes.Status503ServiceUnavailable,
            _ when problemCode.StartsWith("solutions.package.", StringComparison.Ordinal) =>
                StatusCodes.Status422UnprocessableEntity,
            _ when problemCode.StartsWith("solutions.install.", StringComparison.Ordinal) =>
                StatusCodes.Status409Conflict,
            _ => StatusCodes.Status503ServiceUnavailable,
        };
        string safeCode = problemCode is
            "solutions.authorization.denied" or
            "solutions.authorization.workspace_mismatch" or
            "solutions.operation.not_found" or
            "solutions.installation.not_found"
                ? "solutions.resource.not_found"
                : problemCode;
        return Problem(
            safeCode,
            statusCode,
            "The solution request could not be completed.");
    }

    private static IResult Problem(string code, int statusCode, string detail)
    {
        ProblemDetails problem = ProblemDetailsDefaults.CreateProblemDetails(
            statusCode,
            detail,
            code);
        return Results.Json(
            problem,
            statusCode: statusCode,
            contentType: ProblemDetailsDefaults.JsonContentType);
    }

    public sealed record PublishSolutionResponse(
        SolutionVersionSummaryDto Version,
        bool IsRetry);

    public sealed record InstallSolutionResponse(
        SolutionOperationStatusDto Operation,
        bool IsRetry);
}
