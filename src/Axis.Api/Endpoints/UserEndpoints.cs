using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.RegisterUser;
using Axis.Identity.Application.Commands.UpdateUserLanguagePreference;
using Axis.Identity.Application.Queries.GetCurrentUserProfile;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder publicGroup = app.MapGroup("/api/users");

        publicGroup.MapPost("/register", Register)
            .AllowAnonymous()
            .WithName("RegisterUser")
            .WithSummary("Register a standalone user account")
            .WithTags("Identity")
            .Produces<MessageResponse>()
            .ProducesProblem(400)
            .ProducesProblem(409);

        RouteGroupBuilder group = publicGroup.MapGroup("").RequireAuthorization();

        group.MapGet("/me", GetMe)
            .WithName("GetMe")
            .WithSummary("Get the current user's profile")
            .WithTags("Identity")
            .Produces<CurrentUserProfileDto>()
            .ProducesProblem(401)
            .ProducesProblem(404);

        group.MapPut("/me/preferences/language", UpdateLanguagePreference)
            .WithName("UpdateLanguagePreference")
            .WithSummary("Update the current user's language preference")
            .WithTags("Identity")
            .Produces<LanguagePreferenceDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404);

        return app;
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterUserRequest request,
        HttpContext httpContext,
        ISender mediator,
        CancellationToken ct)
    {
        string? idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();

        Result result = await mediator.Send(new RegisterUserCommand(
            request.FullName,
            request.Email,
            request.Password,
            request.PasswordConfirmation,
            request.AcceptedTermsVersion,
            request.AcceptedPrivacyVersion,
            idempotencyKey), ct);

        if (result.IsFailure)
            return result.ToProblemDetails();

        return Results.Ok(new MessageResponse(
            "Registration successful. Please check your email to verify your account."));
    }

    private static async Task<IResult> GetMe(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        CurrentUserProfileDto? profile = await mediator.Send(
            new GetCurrentUserProfileQuery(
                currentUser.UserId,
                currentUser.WorkspaceIdOrNull),
            ct);
        if (profile is null)
            return Results.NotFound();

        return Results.Ok(profile);
    }

    private static async Task<IResult> UpdateLanguagePreference(
        [FromBody] UpdateUserLanguagePreferenceRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<LanguagePreferenceDto> result = await mediator.Send(
            new UpdateUserLanguagePreferenceCommand(
                currentUser.UserId,
                request.Language),
            ct);

        if (result.IsFailure)
            return result.ToProblemDetails();

        return Results.Ok(result.Value);
    }
}
