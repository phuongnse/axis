using System.Security.Claims;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Queries.ListEligibleWorkspaces;
using MediatR;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Endpoints;

public static class WorkspaceContextEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceContextEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/workspace-context")
            .ExcludeFromDescription();

        group.MapGet("/eligible", ListEligible).RequireAuthorization(SourceSessionPolicy);
        group.MapPost("/begin", Begin).RequireAuthorization(SourceSessionPolicy);
        group.MapPost("/confirm", Confirm).RequireAuthorization(SourceSessionPolicy);
        group.MapPost("/recover", Recover)
            .RequireAuthorization(policy =>
            {
                policy.AddAuthenticationSchemes(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    AxisApiServiceExtensions.WorkspaceTransitionScheme);
                policy.RequireAuthenticatedUser();
            })
            .WithMetadata(WorkspaceTransitionRecoveryMetadata.Instance);

        return app;
    }

    private static async Task<IResult> ListEligible(
        HttpContext httpContext,
        ISender mediator,
        CancellationToken ct)
    {
        if (!TryReadSourceSession(httpContext.User, out BrowserSourceSession? source))
            return Results.Unauthorized();

        IReadOnlyList<EligibleWorkspaceDto> workspaces = await mediator.Send(
            new ListEligibleWorkspacesQuery(source!.UserId, source.WorkspaceId),
            ct);
        return Results.Ok(workspaces);
    }

    private static async Task<IResult> Begin(
        [FromBody] BeginWorkspaceContextTransitionRequest request,
        HttpContext httpContext,
        WorkspaceContextTransitionSaga saga,
        CancellationToken ct) =>
        await saga.BeginAsync(request, httpContext, ct);

    private static async Task<IResult> Confirm(
        HttpContext httpContext,
        WorkspaceContextTransitionSaga saga,
        CancellationToken ct) =>
        await saga.ConfirmAsync(httpContext, ct);

    private static async Task<IResult> Recover(
        HttpContext httpContext,
        WorkspaceContextTransitionSaga saga,
        CancellationToken ct) =>
        await saga.RecoverAsync(httpContext, ct);

    private static bool TryReadSourceSession(
        ClaimsPrincipal principal,
        out BrowserSourceSession? source)
    {
        if (!Guid.TryParse(principal.FindFirstValue(Claims.Subject), out Guid userId)
            || !Guid.TryParse(principal.FindFirstValue("workspace_id"), out Guid workspaceId))
        {
            source = null;
            return false;
        }

        string? correlation = principal.FindFirstValue(BrowserSessionCorrelation.ClaimType);
        if (string.IsNullOrWhiteSpace(correlation))
        {
            source = null;
            return false;
        }

        source = new BrowserSourceSession(userId, workspaceId, correlation);
        return true;
    }

    private static void SourceSessionPolicy(AuthorizationPolicyBuilder policy)
    {
        policy.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    }

    private sealed record BrowserSourceSession(
        Guid UserId,
        Guid WorkspaceId,
        string Correlation);
}

public sealed record BeginWorkspaceContextTransitionRequest(Guid TargetWorkspaceId);

public sealed record WorkspaceContextTransitionResponse(
    Guid? TransitionId,
    string Status,
    DateTime? ExpiresAt,
    Guid? AuthoritativeWorkspaceId);

internal sealed class WorkspaceTransitionRecoveryMetadata
{
    public static WorkspaceTransitionRecoveryMetadata Instance { get; } = new();

    private WorkspaceTransitionRecoveryMetadata() { }
}
