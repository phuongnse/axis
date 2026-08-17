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

internal sealed class WorkspaceInvitationDeliveryDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<WorkspaceInvitationDeliveryDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 10;
    private const int MaximumAttempts = 8;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatch(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Workspace invitation delivery batch failed");
            }

            await Task.Delay(PollInterval, timeProvider, stoppingToken);
        }
    }

    internal async Task DispatchBatch(CancellationToken ct)
    {
        IReadOnlyList<(Guid WorkspaceId, Guid InvitationId)> dueKeys;
        using (IServiceScope discoveryScope = scopeFactory.CreateScope())
        {
            IWorkspaceInvitationRepository discovery =
                discoveryScope.ServiceProvider.GetRequiredService<IWorkspaceInvitationRepository>();
            IReadOnlyList<WorkspaceInvitation> due = await discovery.ListDueDeliveryAsync(
                timeProvider.GetUtcNow().UtcDateTime,
                BatchSize,
                ct);
            dueKeys = due.Select(invitation => (invitation.WorkspaceId, invitation.Id)).ToList();
        }

        foreach ((Guid workspaceId, Guid invitationId) in dueKeys)
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IWorkspaceInvitationRepository invitations =
                scope.ServiceProvider.GetRequiredService<IWorkspaceInvitationRepository>();
            WorkspaceInvitation? invitation = await invitations.GetByIdAsync(
                workspaceId,
                invitationId,
                ct);
            if (invitation is not null)
                await DispatchOne(scope.ServiceProvider, invitation, ct);
        }
    }

    private async Task DispatchOne(
        IServiceProvider services,
        WorkspaceInvitation invitation,
        CancellationToken ct)
    {
        IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
        IInvitationDeliveryEnvelopeProtector envelopes =
            services.GetRequiredService<IInvitationDeliveryEnvelopeProtector>();
        IEmailSender emailSender = services.GetRequiredService<IEmailSender>();
        IWorkspaceInvitationRepository invitations =
            services.GetRequiredService<IWorkspaceInvitationRepository>();

        InvitationTokenGeneration token = invitation.CurrentToken;
        string? protectedEnvelope = token.DeliveryEnvelope;
        int generation = token.Generation;
        if (protectedEnvelope is null)
        {
            await MarkTerminalFailure(
                services,
                invitation,
                generation,
                "delivery.envelope_missing",
                ct);
            return;
        }

        try
        {
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            invitation.MarkDeliveryAttempt(
                invitation.Revision,
                now.Add(Backoff(token.DeliveryAttempts + 1)),
                null);
            invitation.RecordModification(
                ActorSnapshot.System(),
                now);
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (ConcurrencyException)
        {
            uow.ClearTracking();
            return;
        }

        try
        {
            InvitationDeliveryMessage message = envelopes.Unprotect(protectedEnvelope);
            if (message.InvitationId != invitation.Id || message.Generation != generation)
                throw new InvalidOperationException("Invitation delivery envelope does not match its generation.");

            await emailSender.SendWorkspaceInvitationEmailAsync(message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Workspace invitation delivery failed for generation {Generation}",
                generation);
            WorkspaceInvitation? failed = await invitations.GetByIdAsync(
                invitation.WorkspaceId,
                invitation.Id,
                ct);
            if (failed is not null && failed.CurrentToken.Generation == generation)
            {
                if (failed.CurrentToken.DeliveryAttempts >= MaximumAttempts)
                {
                    await MarkTerminalFailure(
                        services,
                        failed,
                        generation,
                        "delivery.retry_exhausted",
                        ct);
                }
                else
                {
                    DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                    failed.RecordDeliveryFailure(
                        failed.Revision,
                        now.Add(Backoff(failed.CurrentToken.DeliveryAttempts)),
                        "delivery.transient");
                    failed.RecordModification(
                        ActorSnapshot.System(),
                        now);
                    await SaveIgnoringWinner(uow, ct);
                }
            }
            return;
        }

        WorkspaceInvitation? delivered = await invitations.GetByIdAsync(
            invitation.WorkspaceId,
            invitation.Id,
            ct);
        if (delivered is null
            || delivered.CurrentToken.Generation != generation
            || delivered.Status != WorkspaceInvitationStatus.Pending)
        {
            return;
        }

        DateTime deliveredAt = timeProvider.GetUtcNow().UtcDateTime;
        delivered.MarkDelivered(delivered.Revision);
        delivered.RecordModification(
            ActorSnapshot.System(),
            deliveredAt);
        await EnqueueDeliveryAudit(
            services,
            delivered,
            generation,
            "succeeded",
            ct);
        await SaveIgnoringWinner(uow, ct);
    }

    private async Task MarkTerminalFailure(
        IServiceProvider services,
        WorkspaceInvitation invitation,
        int generation,
        string errorCode,
        CancellationToken ct)
    {
        if (invitation.CurrentToken.Generation != generation
            || invitation.CurrentToken.DeliveryStatus != InvitationDeliveryStatus.Pending)
        {
            return;
        }

        DateTime failedAt = timeProvider.GetUtcNow().UtcDateTime;
        invitation.MarkTerminalDeliveryFailure(invitation.Revision, errorCode);
        invitation.RecordModification(
            ActorSnapshot.System(),
            failedAt);
        await EnqueueDeliveryAudit(
            services,
            invitation,
            generation,
            "terminal_failure",
            ct);
        await SaveIgnoringWinner(services.GetRequiredService<IUnitOfWork>(), ct);
    }

    private async Task EnqueueDeliveryAudit(
        IServiceProvider services,
        WorkspaceInvitation invitation,
        int generation,
        string outcome,
        CancellationToken ct)
    {
        IIdentityAuditOutbox auditOutbox = services.GetRequiredService<IIdentityAuditOutbox>();
        await auditOutbox.EnqueueAsync(
            new AuditEventV1(
                Guid.NewGuid(),
                AuditActorKindV1.System,
                null,
                invitation.InviterUserId,
                invitation.WorkspaceId,
                "workspace.invitation.delivery",
                "WorkspaceInvitation",
                invitation.Id,
                outcome,
                timeProvider.GetUtcNow(),
                $"{invitation.CurrentToken.DeliveryCorrelation}:{outcome}",
                new Dictionary<string, string>
                {
                    ["organizationId"] = invitation.OrganizationId.ToString(),
                    ["workspaceId"] = invitation.WorkspaceId.ToString(),
                    ["requestedRole"] = invitation.RequestedRole.ToString(),
                    ["generation"] = generation.ToString(),
                }),
            ct);
    }

    private static async Task SaveIgnoringWinner(IUnitOfWork uow, CancellationToken ct)
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

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Math.Max(0, attempt - 1)), MaximumBackoff.TotalSeconds));
}
