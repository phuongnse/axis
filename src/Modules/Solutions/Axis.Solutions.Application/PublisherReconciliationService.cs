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
            IReadOnlyList<string> currentPublishers = await ledger.ListPublisherIdsAsync(cancellationToken);
            foreach (string publisherId in currentPublishers
                .Concat(candidate.Select(value => value.PublisherId))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal))
            {
                await unitOfWork.AcquirePublisherFenceAsync(publisherId, cancellationToken);
            }

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
