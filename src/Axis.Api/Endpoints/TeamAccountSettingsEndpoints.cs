using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.CancelTeamAccountDeletion;
using Axis.Identity.Application.Commands.ScheduleTeamAccountDeletion;
using Axis.Identity.Application.Commands.UpdateTeamAccountProfile;
using Axis.Identity.Application.Queries.GetTeamAccountSettings;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class TeamAccountSettingsEndpoints
{
    public static IEndpointRouteBuilder MapTeamAccountSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/team-accounts/current")
            .RequireAuthorization();

        group.MapGet("/settings", GetSettings)
            .RequireAuthorization(Permissions.TeamAccount.SettingsRead)
            .WithName("GetTeamAccountSettings")
            .WithSummary("Get team account settings and usage stats")
            .WithTags("Identity")
            .Produces<TeamAccountSettingsDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/profile", UpdateProfile)
            .RequireAuthorization(Permissions.TeamAccount.SettingsWrite)
            .WithName("UpdateTeamAccountProfile")
            .WithSummary("Update team account profile (name, timezone, language, logo)")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(422);

        group.MapPost("/deletion", ScheduleDeletion)
            .RequireAuthorization(Permissions.TeamAccount.Delete)
            .WithName("ScheduleTeamAccountDeletion")
            .WithSummary("Schedule team account for deletion after a 30-day grace period")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(422);

        group.MapDelete("/deletion", CancelDeletion)
            .RequireAuthorization(Permissions.TeamAccount.Delete)
            .WithName("CancelTeamAccountDeletion")
            .WithSummary("Cancel a scheduled team account deletion")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(422);

        return app;
    }

    private static async Task<IResult> GetSettings(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        TeamAccountSettingsDto? settings = await mediator.Send(
            new GetTeamAccountSettingsQuery(currentUser.TeamAccountId),
            ct);

        if (settings is null)
            return Results.NotFound();

        return Results.Ok(settings);
    }

    private static async Task<IResult> UpdateProfile(
        [FromBody] UpdateTeamAccountProfileRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        byte[]? logoBytes = null;
        string? logoContentType = null;

        if (request.LogoBase64 is not null)
        {
            try
            {
                logoBytes = Convert.FromBase64String(request.LogoBase64);
            }
            catch (FormatException)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["logoBase64"] = ["Logo must be valid Base64-encoded data."],
                    },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            logoContentType = request.LogoContentType ?? "image/png";
        }

        Result result = await mediator.Send(new UpdateTeamAccountProfileCommand(
            currentUser.TeamAccountId,
            request.Name,
            request.TimeZoneId,
            request.DefaultLanguage,
            logoBytes,
            logoContentType), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> ScheduleDeletion(
        [FromBody] ScheduleTeamAccountDeletionRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new ScheduleTeamAccountDeletionCommand(
            currentUser.TeamAccountId,
            currentUser.UserId,
            request.ConfirmationName), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> CancelDeletion(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new CancelTeamAccountDeletionCommand(currentUser.TeamAccountId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }
}

public sealed record UpdateTeamAccountProfileRequest(
    string Name,
    string? TimeZoneId,
    string? DefaultLanguage,
    string? LogoBase64,
    string? LogoContentType);

public sealed record ScheduleTeamAccountDeletionRequest(string ConfirmationName);
