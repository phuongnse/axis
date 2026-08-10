using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application;
using Axis.Identity.Application.Commands.ManageServiceIdentity;
using Axis.Identity.Application.Queries.GetServiceIdentity;
using Axis.Identity.Application.Queries.ListServiceIdentities;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class ServiceIdentityEndpoints
{
    public static IEndpointRouteBuilder MapServiceIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/service-identities")
            .RequireAuthorization(AxisApiServiceExtensions.WorkspaceAccessPolicy)
            .WithTags("Identity");
        group.MapPost("", Create)
            .WithName("CreateServiceIdentity")
            .WithSummary("Create one current-Workspace service identity")
            .Produces<ServiceIdentityDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("", List)
            .WithName("ListServiceIdentities")
            .WithSummary("List non-secret service identity lifecycle state for the current Workspace")
            .Produces<IReadOnlyList<ServiceIdentityDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);
        group.MapGet("/{serviceIdentityId:guid}", Get)
            .WithName("GetServiceIdentity")
            .WithSummary("Get non-secret lifecycle state for one current-Workspace service identity")
            .Produces<ServiceIdentityDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{serviceIdentityId:guid}/keys", AddKey)
            .WithName("AddServiceIdentityKey")
            .WithSummary("Add one ES256 public JWK to a current-Workspace service identity")
            .Produces<ServiceIdentityDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapPost("/{serviceIdentityId:guid}/keys/{keyId:guid}/revoke", RevokeKey)
            .WithName("RevokeServiceIdentityKey")
            .WithSummary("Irrevocably revoke one service identity public key")
            .Produces<ServiceIdentityDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapPost("/{serviceIdentityId:guid}/revoke", Revoke)
            .WithName("RevokeServiceIdentity")
            .WithSummary("Irrevocably revoke one current-Workspace service identity")
            .Produces<ServiceIdentityDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    private static async Task<IResult> Create(
        [FromBody] CreateRequest request, CurrentUser user, HttpContext context, ISender sender, CancellationToken ct) =>
        (await sender.Send(new CreateServiceIdentityCommand(user.UserId, user.WorkspaceId, request.ClientId, Correlation(context)), ct)).ToProblemOrOk();
    private static async Task<IResult> Get(Guid serviceIdentityId, CurrentUser user, ISender sender, CancellationToken ct) =>
        (await sender.Send(new GetServiceIdentityQuery(user.UserId, user.WorkspaceId, serviceIdentityId), ct)).ToProblemOrOk();
    private static async Task<IResult> List(CurrentUser user, ISender sender, CancellationToken ct) =>
        (await sender.Send(new ListServiceIdentitiesQuery(user.UserId, user.WorkspaceId), ct)).ToProblemOrOk();
    private static async Task<IResult> AddKey(Guid serviceIdentityId, [FromBody] KeyRequest request, CurrentUser user, HttpContext context, ISender sender, CancellationToken ct) =>
        (await sender.Send(new AddServiceIdentityKeyCommand(user.UserId, user.WorkspaceId, serviceIdentityId, request.ExpectedRevision, request.PublicJwk, Correlation(context)), ct)).ToProblemOrOk();
    private static async Task<IResult> RevokeKey(Guid serviceIdentityId, Guid keyId, [FromBody] RevisionRequest request, CurrentUser user, HttpContext context, ISender sender, CancellationToken ct) =>
        (await sender.Send(new RevokeServiceIdentityKeyCommand(user.UserId, user.WorkspaceId, serviceIdentityId, keyId, request.ExpectedRevision, Correlation(context)), ct)).ToProblemOrOk();
    private static async Task<IResult> Revoke(Guid serviceIdentityId, [FromBody] RevisionRequest request, CurrentUser user, HttpContext context, ISender sender, CancellationToken ct) =>
        (await sender.Send(new RevokeServiceIdentityCommand(user.UserId, user.WorkspaceId, serviceIdentityId, request.ExpectedRevision, Correlation(context)), ct)).ToProblemOrOk();
    private static string Correlation(HttpContext context) => context.TraceIdentifier;
    public sealed record CreateRequest(string ClientId);
    public sealed record KeyRequest(int ExpectedRevision, string PublicJwk);
    public sealed record RevisionRequest(int ExpectedRevision);
    private static IResult ToProblemOrOk<T>(this Result<T> result) => result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
}
