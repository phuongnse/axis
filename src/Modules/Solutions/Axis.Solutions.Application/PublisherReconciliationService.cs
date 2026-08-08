namespace Axis.Solutions.Application;

public sealed class PublisherReconciliationService(
    ITrustedPublisherLedger ledger,
    SolutionOrchestrator orchestrator,
    ISolutionsUnitOfWork unitOfWork)
{
    public async Task ReconcileAsync(long configurationRevision, IReadOnlyList<TrustedPublisherConfigurationKey> candidate, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            IReadOnlyList<TrustedPublisherIdentity> revoked = await ledger.ReconcileAsync(configurationRevision, candidate, cancellationToken);
            foreach (TrustedPublisherIdentity key in revoked)
                await orchestrator.MarkRevokedNoncompliantAsync(key.PublisherId, key.KeyId, now, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
