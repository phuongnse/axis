using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.CancelOrganizationDeletion;
using Axis.Identity.Application.Commands.ScheduleOrganizationDeletion;
using Axis.Identity.Application.Commands.UpdateOrganizationProfile;
using Axis.Identity.Application.Queries.GetOrganizationSettings;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class OrganizationSettingsEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/organizations/current")
            .RequireAuthorization();

        group.MapGet("/settings", GetSettings)
            .RequireAuthorization(Permissions.Organization.SettingsRead)
            .WithName("GetOrganizationSettings")
            .WithSummary("Get organization settings and usage stats")
            .WithTags("Identity")
            .Produces<object>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/profile", UpdateProfile)
            .RequireAuthorization(Permissions.Organization.SettingsWrite)
            .WithName("UpdateOrganizationProfile")
            .WithSummary("Update organization profile (name, timezone, language, logo)")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(422);

        group.MapPost("/deletion", ScheduleDeletion)
            .RequireAuthorization(Permissions.Organization.Delete)
            .WithName("ScheduleOrganizationDeletion")
            .WithSummary("Schedule organization for deletion after a 30-day grace period")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(422);

        group.MapDelete("/deletion", CancelDeletion)
            .RequireAuthorization(Permissions.Organization.Delete)
            .WithName("CancelOrganizationDeletion")
            .WithSummary("Cancel a scheduled organization deletion")
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
        OrganizationSettingsDto? settings = await mediator.Send(
            new GetOrganizationSettingsQuery(currentUser.OrgId),
            ct);

        if (settings is null)
            return Results.NotFound();

        return Results.Ok(new
        {
            organization_id = settings.OrganizationId,
            name = settings.Name,
            slug = settings.Slug,
            logo_url = settings.LogoUrl,
            plan_name = settings.PlanName,
            status = settings.Status,
            created_at = settings.CreatedAt,
            time_zone_id = settings.TimeZoneId,
            default_language = settings.DefaultLanguage,
            scheduled_hard_delete_at = settings.ScheduledHardDeleteAt,
            usage = new
            {
                workflows_used = settings.Usage.WorkflowsUsed,
                workflows_limit = settings.Usage.WorkflowsLimit,
                executions_used_this_month = settings.Usage.ExecutionsUsedThisMonth,
                executions_per_month_limit = settings.Usage.ExecutionsPerMonthLimit,
                users_used = settings.Usage.UsersUsed,
                users_limit = settings.Usage.UsersLimit,
            },
        });
    }

    private static async Task<IResult> UpdateProfile(
        [FromBody] UpdateOrganizationProfileRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        byte[]? logoBytes = null;
        string? logoContentType = null;

        if (request.LogoBase64 is not null)
        {
            logoBytes = Convert.FromBase64String(request.LogoBase64);
            logoContentType = request.LogoContentType ?? "image/png";
        }

        Result result = await mediator.Send(new UpdateOrganizationProfileCommand(
            currentUser.OrgId,
            request.Name,
            request.TimeZoneId,
            request.DefaultLanguage,
            logoBytes,
            logoContentType), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> ScheduleDeletion(
        [FromBody] ScheduleOrganizationDeletionRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new ScheduleOrganizationDeletionCommand(
            currentUser.OrgId,
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
        Result result = await mediator.Send(new CancelOrganizationDeletionCommand(currentUser.OrgId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }
}

public sealed record UpdateOrganizationProfileRequest(
    string Name,
    string? TimeZoneId,
    string? DefaultLanguage,
    string? LogoBase64,
    string? LogoContentType);

public sealed record ScheduleOrganizationDeletionRequest(string ConfirmationName);
