using Axis.Solutions.Application;
using Axis.Solutions.Domain;
using Axis.Solutions.Infrastructure.Persistence;
using Axis.Solutions.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Solutions.Infrastructure.Repositories;

internal sealed class TrustedPublisherLedger(SolutionsDbContext context) : ITrustedPublisherLedger
{
    public async Task<IReadOnlyList<string>> ListPublisherIdsAsync(CancellationToken cancellationToken = default) =>
        await context.TrustedPublisherKeys.AsNoTracking()
            .Select(value => value.PublisherId)
            .Distinct()
            .OrderBy(value => value)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TrustedPublisherIdentity>> ReconcileAsync(long configurationRevision, IReadOnlyList<TrustedPublisherConfigurationKey> candidate, CancellationToken cancellationToken = default)
    {
        if (configurationRevision < 0 ||
            configurationRevision == 0 && candidate.Count != 0 ||
            candidate.GroupBy(x => (x.PublisherId, x.KeyId)).Any(x => x.Count() != 1))
            throw new InvalidOperationException("solutions.publisher_configuration.invalid");
        try
        {
            Dictionary<(string, string), (TrustedPublisherConfigurationKey Configuration, TrustedPublisherKey Key)> validated = [];
            foreach (TrustedPublisherConfigurationKey value in candidate)
            {
                if (string.IsNullOrWhiteSpace(value.PublisherId) || string.IsNullOrWhiteSpace(value.KeyId) || string.IsNullOrWhiteSpace(value.PublicKeyPem))
                    throw new InvalidOperationException("solutions.publisher_configuration.invalid");
                validated[(value.PublisherId, value.KeyId)] = (value,
                    TrustedPublisherKey.Create(value.PublisherId, value.KeyId, value.PublicKeyPem, configurationRevision));
            }

            TrustedPublisherLedgerStateRecord? state = await context.TrustedPublisherLedgerState.SingleOrDefaultAsync(cancellationToken);
            List<TrustedPublisherKey> current = await context.TrustedPublisherKeys.ToListAsync(cancellationToken);
            if (configurationRevision == 0)
            {
                if (state is not null || current.Count != 0)
                    throw new InvalidOperationException("solutions.publisher_configuration.revision_conflict");
                return [];
            }
            if (state is not null && configurationRevision < state.ActiveRevision)
                throw new InvalidOperationException("solutions.publisher_configuration.revision_not_monotonic");

            if (state is not null && configurationRevision == state.ActiveRevision)
            {
                EnsureCanonicalRetry(current, validated);
                return [];
            }

            Dictionary<(string, string), TrustedPublisherConfigurationKey> proposed = candidate.ToDictionary(x => (x.PublisherId, x.KeyId));
            List<TrustedPublisherIdentity> revoked = [];
            foreach (TrustedPublisherKey existing in current)
            {
                if (!proposed.Remove((existing.PublisherId, existing.KeyId), out TrustedPublisherConfigurationKey? next) || !next.IsActive)
                {
                    if (!existing.IsTombstone)
                    {
                        existing.Revoke(configurationRevision);
                        revoked.Add(new TrustedPublisherIdentity(existing.PublisherId, existing.KeyId));
                    }
                }
                else
                    existing.ReconcileActive(next.PublicKeyPem, configurationRevision);
            }
            foreach (TrustedPublisherConfigurationKey next in proposed.Values)
            {
                if (!next.IsActive)
                    throw new InvalidOperationException("solutions.publisher_configuration.unknown_revocation");
                await context.TrustedPublisherKeys.AddAsync(TrustedPublisherKey.Create(next.PublisherId, next.KeyId, next.PublicKeyPem, configurationRevision), cancellationToken);
            }

            if (state is null)
                await context.TrustedPublisherLedgerState.AddAsync(new TrustedPublisherLedgerStateRecord { Id = 1, ActiveRevision = configurationRevision }, cancellationToken);
            else
                state.ActiveRevision = configurationRevision;

            await context.SaveChangesAsync(cancellationToken);
            return revoked;
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private static void EnsureCanonicalRetry(
        IReadOnlyList<TrustedPublisherKey> current,
        IReadOnlyDictionary<(string, string), (TrustedPublisherConfigurationKey Configuration, TrustedPublisherKey Key)> candidate)
    {
        Dictionary<(string, string), TrustedPublisherKey> existing = current.ToDictionary(x => (x.PublisherId, x.KeyId));
        foreach (((string, string) identity, (TrustedPublisherConfigurationKey configuration, TrustedPublisherKey key)) in candidate)
        {
            if (!existing.TryGetValue(identity, out TrustedPublisherKey? stored) ||
                stored.SpkiSha256 != key.SpkiSha256 ||
                configuration.IsActive != (stored.Status == TrustedPublisherKeyStatus.Active && !stored.IsTombstone))
                throw new InvalidOperationException("solutions.publisher_configuration.revision_conflict");
        }

        if (current.Any(x => x.Status == TrustedPublisherKeyStatus.Active &&
            !candidate.ContainsKey((x.PublisherId, x.KeyId))))
            throw new InvalidOperationException("solutions.publisher_configuration.revision_conflict");
    }
}
