using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class WorkspaceInvitationLifecycleWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<WorkspaceInvitationLifecycleWorker> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatch(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Workspace invitation lifecycle batch failed");
            }

            await Task.Delay(PollInterval, timeProvider, stoppingToken);
        }
    }

    internal async Task ProcessBatch(CancellationToken ct)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        await ExpireDue(now, ct);
        await PurgeReady(now, ct);
    }

    private async Task ExpireDue(DateTime now, CancellationToken ct)
    {
        IReadOnlyList<(Guid WorkspaceId, Guid InvitationId)> due;
        using (IServiceScope discoveryScope = scopeFactory.CreateScope())
        {
            IWorkspaceInvitationRepository repository =
                discoveryScope.ServiceProvider.GetRequiredService<IWorkspaceInvitationRepository>();
            due = (await repository.ListDueExpiryAsync(now, BatchSize, ct))
                .Select(invitation => (invitation.WorkspaceId, invitation.Id))
                .ToList();
        }

        foreach ((Guid workspaceId, Guid invitationId) in due)
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IWorkspaceInvitationRepository repository =
                scope.ServiceProvider.GetRequiredService<IWorkspaceInvitationRepository>();
            WorkspaceInvitation? invitation = await repository.GetByIdAsync(
                workspaceId,
                invitationId,
                ct);
            if (invitation is null
                || invitation.Status != WorkspaceInvitationStatus.Pending
                || invitation.ExpiresAt > now)
            {
                continue;
            }

            invitation.Expire(invitation.Revision, now);
            invitation.RecordModification(
                ActorSnapshot.System(),
                now);
            await scope.ServiceProvider.GetRequiredService<IIdentityAuditOutbox>().EnqueueAsync(
                CreateExpiryAudit(invitation, now),
                ct);
            await SaveIgnoringConcurrentWinner(
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                ct);
        }
    }

    private async Task PurgeReady(DateTime now, CancellationToken ct)
    {
        IReadOnlyList<(Guid WorkspaceId, Guid InvitationId)> ready;
        using (IServiceScope discoveryScope = scopeFactory.CreateScope())
        {
            IWorkspaceInvitationRepository repository =
                discoveryScope.ServiceProvider.GetRequiredService<IWorkspaceInvitationRepository>();
            ready = (await repository.ListReadyForTerminalCleanupAsync(BatchSize, ct))
                .Select(invitation => (invitation.WorkspaceId, invitation.Id))
                .ToList();
        }

        foreach ((Guid workspaceId, Guid invitationId) in ready)
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IWorkspaceInvitationRepository repository =
                scope.ServiceProvider.GetRequiredService<IWorkspaceInvitationRepository>();
            WorkspaceInvitation? invitation = await repository.GetByIdAsync(
                workspaceId,
                invitationId,
                ct);
            if (invitation is null
                || invitation.Status == WorkspaceInvitationStatus.Pending
                || invitation.TerminalMaterialPurgedAt is not null)
            {
                continue;
            }

            invitation.PurgeTerminalMaterial(invitation.Revision, now);
            invitation.RecordModification(
                ActorSnapshot.System(),
                now);
            await SaveIgnoringConcurrentWinner(
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                ct);
        }
    }

    private AuditEventV1 CreateExpiryAudit(WorkspaceInvitation invitation, DateTime now) =>
        new(
            Guid.NewGuid(),
            AuditActorKindV1.System,
            null,
            invitation.InviterUserId,
            invitation.WorkspaceId,
            "workspace.invitation.expired",
            "WorkspaceInvitation",
            invitation.Id,
            "succeeded",
            new DateTimeOffset(now, TimeSpan.Zero),
            $"invitation-expiry:{invitation.Id:N}:{invitation.Revision}",
            new Dictionary<string, string>
            {
                ["organizationId"] = invitation.OrganizationId.ToString(),
                ["workspaceId"] = invitation.WorkspaceId.ToString(),
                ["requestedRole"] = invitation.RequestedRole.ToString(),
                ["generation"] = invitation.CurrentToken.Generation.ToString(),
            });

    private static async Task SaveIgnoringConcurrentWinner(IUnitOfWork uow, CancellationToken ct)
    {
        try
        {
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (ConcurrencyException)
        {
            uow.ClearTracking();
        }
    }
}
