using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Identity.Infrastructure.Services;

/// <summary>Backfills workspace settings permissions onto existing system Admin roles.</summary>
internal sealed class WorkspaceSettingsPermissionSeeder(
    IServiceProvider serviceProvider,
    ILogger<WorkspaceSettingsPermissionSeeder> logger) : IHostedService
{
    private static readonly string[] WorkspaceSettingsPermissions =
    [
        "workspace:settings:read",
        "workspace:settings:write",
        "workspace:delete",
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
        IdentityDbContext context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        List<Role> adminRoles = await context.Roles
            .Where(r => r.IsSystem && r.Name == "Admin")
            .ToListAsync(cancellationToken);

        int updated = 0;
        foreach (Role role in adminRoles)
        {
            if (role.GrantMissingPermissions(WorkspaceSettingsPermissions))
                updated++;
        }

        if (updated > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Backfilled workspace settings permissions on {Count} Admin role(s).",
                updated);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
