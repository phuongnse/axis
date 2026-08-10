using System.Security.Claims;
using Axis.Api.Endpoints;
using Axis.Api.Extensions;
using Axis.Api.Middleware;
using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Identity.Application.Commands.CompensateWorkspaceContextTransition;
using Axis.Identity.Application.Commands.CompleteWorkspaceContextTransition;
using Axis.Identity.Application.Commands.FailWorkspaceContextTransition;
using Axis.Identity.Application.Queries.GetUserTokenClaims;
using Axis.Identity.Application.Queries.ResolveWorkspaceContextTransition;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Infrastructure;

internal sealed class WorkspaceContextTransitionSaga(
    ISender mediator,
    AxisBrowserSessionIssuer sessionIssuer)
{
    private const string AntiforgeryCookieName = "__Host-axis-antiforgery";
    private const string TransitionCookieName = "__Host-axis-workspace-transition";

    public async Task<IResult> BeginAsync(
        BeginWorkspaceContextTransitionRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!TryReadSourceSession(httpContext.User, out BrowserSourceSession? source))
            return Results.Unauthorized();

        string targetCorrelation = BrowserSessionCorrelation.Generate();
        Result<WorkspaceContextTransitionDto> result = await mediator.Send(
            new BeginWorkspaceContextTransitionCommand(
                source!.UserId,
                source.WorkspaceId,
                request.TargetWorkspaceId,
                BrowserSessionCorrelation.Digest(source.Correlation),
                BrowserSessionCorrelation.Digest(targetCorrelation),
                CorrelationId(httpContext)),
            ct);
        if (result.IsFailure)
            return result.ToProblemDetails();

        WorkspaceContextTransitionDto transition = result.Value;
        WorkspaceTransitionTicket ticket = new(
            transition.TransitionId,
            source.UserId,
            source.WorkspaceId,
            transition.TargetWorkspaceId,
            source.Correlation,
            targetCorrelation);
        try
        {
            await httpContext.SignInAsync(
                AxisApiServiceExtensions.WorkspaceTransitionScheme,
                ticket.CreatePrincipal(),
                new AuthenticationProperties
                {
                    AllowRefresh = false,
                    ExpiresUtc = new DateTimeOffset(transition.ExpiresAt),
                    IsPersistent = false,
                });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Result<WorkspaceContextTransitionDto> failure = await mediator.Send(
                new FailWorkspaceContextTransitionCommand(
                    transition.TransitionId,
                    source.UserId,
                    BrowserSessionCorrelation.Digest(source.Correlation),
                    CorrelationId(httpContext)),
                ct);
            return failure.IsFailure
                ? failure.ToProblemDetails()
                : Results.Problem(
                    detail: "The target Workspace context could not be staged.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new WorkspaceContextTransitionResponse(
            transition.TransitionId,
            transition.Status,
            transition.ExpiresAt,
            null));
    }

    public async Task<IResult> ConfirmAsync(HttpContext httpContext, CancellationToken ct)
    {
        if (!TryReadSourceSession(httpContext.User, out BrowserSourceSession? source))
            return Results.Unauthorized();

        AuthenticateResult transitionResult = await httpContext.AuthenticateAsync(
            AxisApiServiceExtensions.WorkspaceTransitionScheme);
        if (!WorkspaceTransitionTicket.TryRead(transitionResult, out WorkspaceTransitionTicket? ticket)
            || !Matches(source!, ticket!))
        {
            return Results.NotFound();
        }

        Result<WorkspaceContextTransitionDto> completion = await mediator.Send(
            new CompleteWorkspaceContextTransitionCommand(
                ticket!.TransitionId,
                ticket.UserId,
                BrowserSessionCorrelation.Digest(ticket.TargetCorrelation),
                CorrelationId(httpContext)),
            ct);
        if (completion.IsFailure)
            return completion.ToProblemDetails();
        if (!StringComparer.Ordinal.Equals(completion.Value.Status, "Completed"))
        {
            return await EstablishSourceAfterTerminalTransitionAsync(
                httpContext,
                source!,
                completion.Value,
                ct);
        }

        return await EstablishCompletedTargetAsync(httpContext, ticket, completion.Value, ct);
    }

    public async Task<IResult> RecoverAsync(HttpContext httpContext, CancellationToken ct)
    {
        TryReadSourceSession(httpContext.User, out BrowserSourceSession? source);
        AuthenticateResult ticketResult = await httpContext.AuthenticateAsync(
            AxisApiServiceExtensions.WorkspaceTransitionScheme);
        WorkspaceTransitionTicket.TryRead(ticketResult, out WorkspaceTransitionTicket? ticket);
        if (ticket is not null)
        {
            BrowserSourceSession ticketSource = new(
                ticket.UserId,
                ticket.SourceWorkspaceId,
                ticket.SourceCorrelation);
            if (source is not null && !Matches(source, ticket))
                return Results.NotFound();
            source = ticketSource;
        }
        if (source is null)
            return Results.Unauthorized();

        Result<WorkspaceContextTransitionDto> resolved = await mediator.Send(
            new ResolveWorkspaceContextTransitionQuery(
                source.UserId,
                BrowserSessionCorrelation.Digest(ticket?.TargetCorrelation ?? source.Correlation),
                ticket is null
                    ? WorkspaceContextTransitionCorrelationRole.Source
                    : WorkspaceContextTransitionCorrelationRole.Target),
            ct);
        if (resolved.IsFailure && resolved.ErrorCode == ErrorCodes.NotFound)
        {
            return Results.Ok(new WorkspaceContextTransitionResponse(
                null,
                "None",
                null,
                source.WorkspaceId));
        }
        if (resolved.IsFailure)
            return resolved.ToProblemDetails();

        WorkspaceContextTransitionDto transition = resolved.Value;
        if (StringComparer.Ordinal.Equals(transition.Status, "Pending"))
        {
            Result<WorkspaceContextTransitionDto> compensated = await mediator.Send(
                new CompensateWorkspaceContextTransitionCommand(
                    transition.TransitionId,
                    source.UserId,
                    BrowserSessionCorrelation.Digest(source.Correlation),
                    CorrelationId(httpContext)),
                ct);
            if (compensated.IsFailure)
                return compensated.ToProblemDetails();
            transition = compensated.Value;
        }

        if (StringComparer.Ordinal.Equals(transition.Status, "Completed"))
        {
            if (ticket is null || ticket.TransitionId != transition.TransitionId)
            {
                return Results.Problem(
                    detail: "The completed target session is unavailable. Sign out and sign in again.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            return await EstablishCompletedTargetAsync(httpContext, ticket, transition, ct);
        }

        return await EstablishSourceAfterTerminalTransitionAsync(httpContext, source, transition, ct);
    }

    private async Task<IResult> EstablishSourceAfterTerminalTransitionAsync(
        HttpContext httpContext,
        BrowserSourceSession source,
        WorkspaceContextTransitionDto transition,
        CancellationToken ct)
    {
        Result<UserTokenClaimsDto> sourceClaims = await mediator.Send(
            new GetUserTokenClaimsQuery(source.UserId, null),
            ct);
        if (sourceClaims.IsFailure)
            return sourceClaims.ToProblemDetails();

        await sessionIssuer.RotateAsync(
            httpContext,
            sourceClaims.Value.UserId,
            source.WorkspaceId,
            sourceClaims.Value.Email,
            sourceClaims.Value.FullName);
        DeleteTransitionCookie(httpContext);
        RotateAntiforgery(httpContext);

        return Results.Ok(new WorkspaceContextTransitionResponse(
            transition.TransitionId,
            transition.Status,
            transition.ExpiresAt,
            source.WorkspaceId));
    }

    private async Task<IResult> EstablishCompletedTargetAsync(
        HttpContext httpContext,
        WorkspaceTransitionTicket ticket,
        WorkspaceContextTransitionDto transition,
        CancellationToken ct)
    {
        Result<UserTokenClaimsDto> targetClaims = await mediator.Send(
            new GetUserTokenClaimsQuery(ticket.UserId, ticket.TargetWorkspaceId),
            ct);
        if (targetClaims.IsFailure)
            return targetClaims.ToProblemDetails();

        await sessionIssuer.RotateAsync(
            httpContext,
            targetClaims.Value.UserId,
            targetClaims.Value.workspaceId,
            targetClaims.Value.Email,
            targetClaims.Value.FullName,
            ticket.TargetCorrelation);
        DeleteTransitionCookie(httpContext);
        RotateAntiforgery(httpContext);

        return Results.Ok(new WorkspaceContextTransitionResponse(
            transition.TransitionId,
            transition.Status,
            transition.ExpiresAt,
            ticket.TargetWorkspaceId));
    }

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

    private static bool Matches(BrowserSourceSession source, WorkspaceTransitionTicket ticket) =>
        source.UserId == ticket.UserId
        && source.WorkspaceId == ticket.SourceWorkspaceId
        && StringComparer.Ordinal.Equals(source.Correlation, ticket.SourceCorrelation);

    private static string CorrelationId(HttpContext context) =>
        context.Items[CorrelationIdMiddleware.HttpContextItemKey] as string
        ?? context.TraceIdentifier;

    private static void RotateAntiforgery(HttpContext context) =>
        context.Response.Cookies.Delete(
            AntiforgeryCookieName,
            new CookieOptions
            {
                Path = "/",
                Secure = true,
                SameSite = SameSiteMode.Strict,
            });

    private static void DeleteTransitionCookie(HttpContext context) =>
        context.Response.Cookies.Delete(
            TransitionCookieName,
            new CookieOptions
            {
                Path = "/",
                Secure = true,
                SameSite = SameSiteMode.Strict,
            });

    private sealed record BrowserSourceSession(Guid UserId, Guid WorkspaceId, string Correlation);
}
