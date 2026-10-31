using Axis.Configuration.Releases;
using Axis.Core.Tenancy;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;

namespace Axis.Server.Console;

/// <summary>
/// The routes of the operator console under <c>/api/console/</c>. Every route of the group is checked in order: signed-in
/// user (401), operator role named by <c>Console:OperatorRole</c> (403, also for everyone when the setting has no value).
/// Application policies play no part. The group adds no CSRF check of its own, so the CSRF middleware covers every write
/// that joins it.
/// <para>
/// <c>GET /api/console/tenant</c> answers the tenant of the host: its <c>id</c>, its <c>status</c> and, for each active
/// application in name order, its active release with the release <c>number</c> and <c>label</c> from the release history
/// and the time it was activated. The status is always <c>active</c>, because a host serves only active tenants.
/// </para>
/// <para>
/// The dead letter routes under <c>/api/console/dead-letters</c> list, redeliver and discard the dead letters of every
/// application of the tenant, see <see cref="ConsoleDeadLetterEndpoints"/>.
/// </para>
/// </summary>
internal static class ConsoleEndpoints
{
    // A host serves only active tenants: a suspended tenant's hosts answer 503, so a request that got here is on an active one.
    private const string ActiveStatus = "active";

    public static void MapConsoleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var console = endpoints.MapGroup("/api/console");
        console.AddEndpointFilter(RequireOperatorAsync);
        console.MapGet("/tenant", GetTenantAsync);
        console.MapConsoleDeadLetterEndpoints();
    }

    private static async ValueTask<object?> RequireOperatorAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        var settings = httpContext.RequestServices.GetRequiredService<ConsoleSettings>();
        return settings.Allows(user.Roles) ? await next(context) : Grants.Forbidden();
    }

    private static async Task<IResult> GetTenantAsync(
        ITenantContextAccessor tenants,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (tenants.Current is not { } tenant)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This host has no tenant.");
        }

        var store = new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken));
        var applications = new List<ConsoleApplication>();
        foreach (var active in await store.ListAsync(cancellationToken))
        {
            // A release can be in the history more than once, always with one number, so the first row of it has it.
            var activation = (await store.ListActivationsAsync(active.ApplicationId, cancellationToken))
                .FirstOrDefault(row => row.ReleaseId == active.ReleaseId);
            applications.Add(new ConsoleApplication(
                active.ApplicationId, active.Name, active.ReleaseId, activation?.Number, activation?.Label, active.ActivatedAt));
        }

        return Results.Ok(new ConsoleTenantResponse(tenant.TenantId, ActiveStatus, applications));
    }
}

internal sealed record ConsoleTenantResponse(string Id, string Status, IReadOnlyList<ConsoleApplication> Applications);

internal sealed record ConsoleApplication(Guid Id, string Name, Guid ReleaseId, int? ReleaseNumber, string? ReleaseLabel, DateTimeOffset ActivatedAt);
