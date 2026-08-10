using System.Security.Claims;
using Axis.Api.Extensions;
using Axis.Api.Middleware;
using Axis.Identity.Application;
using Axis.Identity.Application.Commands.AcceptWorkspaceInvitation;
using Axis.Identity.Application.Commands.ExchangeWorkspaceInvitation;
using Axis.Identity.Application.Queries.HasWorkspaceInvitationHandoff;
using Axis.Identity.Application.Queries.ReviewWorkspaceInvitation;
using Axis.Identity.Application.Services;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Endpoints;

public static class WorkspaceInvitationAcceptanceEndpoints
{
    internal const string HandoffCookieName = "__Host-axis-invitation-handoff";

    public static IEndpointRouteBuilder MapWorkspaceInvitationAcceptanceEndpoints(
        this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/internal/workspace-invitations")
            .ExcludeFromDescription();

        group.MapPost("/exchange", Exchange)
            .AllowAnonymous()
            .RequireRateLimiting("auth");
        group.MapGet("/handoff", HandoffState).AllowAnonymous();
        group.MapGet("/review", Review).RequireAuthorization(BrowserSessionPolicy);
        group.MapPost("/accept", Accept).RequireAuthorization(BrowserSessionPolicy);

        return app;
    }

    private static async Task<IResult> Exchange(
        [FromBody] ExchangeWorkspaceInvitationRequest request,
        HttpContext context,
        ISender mediator,
        WorkspaceInvitationPolicy policy,
        CancellationToken ct)
    {
        Result<WorkspaceInvitationExchangeDto> result = await mediator.Send(
            new ExchangeWorkspaceInvitationCommand(
                request.Token,
                RequestPartition(context),
                CorrelationId(context)),
            ct);
        if (result.IsFailure)
        {
            DeleteHandoffCookie(context);
            return result.ToProblemDetails();
        }

        string handoffSecret = result.Value.HandoffSecret
            ?? throw new InvalidOperationException("Successful invitation exchange omitted the handoff.");
        context.Response.Cookies.Append(
            HandoffCookieName,
            handoffSecret,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/",
                MaxAge = policy.HandoffLifetime,
            });
        return Results.Ok(new WorkspaceInvitationExchangeResponse(result.Value.Outcome));
    }

    private static async Task<IResult> HandoffState(
        HttpContext context,
        ISender mediator,
        CancellationToken ct)
    {
        string? handoffHash = ReadHandoffHash(context);
        bool active = handoffHash is not null
            && await mediator.Send(new HasWorkspaceInvitationHandoffQuery(handoffHash), ct);
        if (!active)
            DeleteHandoffCookie(context);
        return Results.Ok(new WorkspaceInvitationHandoffStateDto(active));
    }

    private static async Task<IResult> Review(
        HttpContext context,
        ISender mediator,
        CancellationToken ct)
    {
        if (!TryReadUserId(context.User, out Guid userId)
            || ReadHandoffHash(context) is not string handoffHash)
        {
            return Results.Unauthorized();
        }

        Result<WorkspaceInvitationReviewDto> result = await mediator.Send(
            new ReviewWorkspaceInvitationQuery(handoffHash, userId, CorrelationId(context)),
            ct);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> Accept(
        HttpContext context,
        ISender mediator,
        CancellationToken ct)
    {
        if (!TryReadUserId(context.User, out Guid userId)
            || ReadHandoffHash(context) is not string handoffHash)
        {
            return Results.Unauthorized();
        }

        Result<WorkspaceInvitationAcceptanceDto> result = await mediator.Send(
            new AcceptWorkspaceInvitationCommand(handoffHash, userId, CorrelationId(context)),
            ct);
        if (result.IsSuccess || result.ProblemCode != IdentityProblemCodes.InvitationAccountMismatch)
            DeleteHandoffCookie(context);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static string? ReadHandoffHash(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(HandoffCookieName, out string? secret)
            || secret.Length != 64
            || !secret.All(Uri.IsHexDigit))
        {
            return null;
        }

        return OpaqueTokenGenerator.Hash(secret);
    }

    private static void DeleteHandoffCookie(HttpContext context) =>
        context.Response.Cookies.Delete(
            HandoffCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/",
            });

    private static bool TryReadUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.GetClaim(Claims.Subject), out userId);

    private static string RequestPartition(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unavailable";

    private static string CorrelationId(HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out object? value)
            && value is string correlationId
                ? correlationId
                : context.TraceIdentifier;

    private static void BrowserSessionPolicy(AuthorizationPolicyBuilder policy)
    {
        policy.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    }
}
