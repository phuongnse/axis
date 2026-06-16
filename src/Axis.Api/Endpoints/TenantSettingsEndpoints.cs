using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.CancelTenantDeletion;
using Axis.Identity.Application.Commands.ScheduleTenantDeletion;
using Axis.Identity.Application.Commands.UpdateTenantProfile;
using Axis.Identity.Application.Queries.GetTenantSettings;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class TenantSettingsEndpoints
{
    public static IEndpointRouteBuilder MapTenantSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/tenants/current")
            .RequireAuthorization();

        group.MapGet("/settings", GetSettings)
            .RequireAuthorization(Permissions.Tenant.SettingsRead)
            .WithName("GetTenantSettings")
            .WithSummary("Get tenant settings and usage stats")
            .WithTags("Identity")
            .Produces<TenantSettingsDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/profile", UpdateProfile)
            .RequireAuthorization(Permissions.Tenant.SettingsWrite)
            .WithName("UpdateTenantProfile")
            .WithSummary("Update tenant profile (name, timezone, language, logo)")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(422);

        group.MapPost("/deletion", ScheduleDeletion)
            .RequireAuthorization(Permissions.Tenant.Delete)
            .WithName("ScheduleTenantDeletion")
            .WithSummary("Schedule tenant for deletion after a 30-day grace period")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(422);

        group.MapDelete("/deletion", CancelDeletion)
            .RequireAuthorization(Permissions.Tenant.Delete)
            .WithName("CancelTenantDeletion")
            .WithSummary("Cancel a scheduled tenant deletion")
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
        TenantSettingsDto? settings = await mediator.Send(
            new GetTenantSettingsQuery(currentUser.TenantId),
            ct);

        if (settings is null)
            return Results.NotFound();

        return Results.Ok(settings);
    }

    private static async Task<IResult> UpdateProfile(
        [FromBody] UpdateTenantProfileRequest request,
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

        Result result = await mediator.Send(new UpdateTenantProfileCommand(
            currentUser.TenantId,
            request.Name,
            request.TimeZoneId,
            request.DefaultLanguage,
            logoBytes,
            logoContentType), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> ScheduleDeletion(
        [FromBody] ScheduleTenantDeletionRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new ScheduleTenantDeletionCommand(
            currentUser.TenantId,
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
        Result result = await mediator.Send(new CancelTenantDeletionCommand(currentUser.TenantId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }
}

public sealed record UpdateTenantProfileRequest(
    string Name,
    string? TimeZoneId,
    string? DefaultLanguage,
    string? LogoBase64,
    string? LogoContentType);

public sealed record ScheduleTenantDeletionRequest(string ConfirmationName);
