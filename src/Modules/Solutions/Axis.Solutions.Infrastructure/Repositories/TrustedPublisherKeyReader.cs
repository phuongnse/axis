using Axis.Solutions.Application;
using Axis.Solutions.Domain;
using Axis.Solutions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Solutions.Infrastructure.Repositories;

internal sealed class TrustedPublisherKeyReader(SolutionsDbContext context) : ITrustedPublisherKeyReader
{
    public async Task<TrustedPublisherSnapshot?> FindAsync(string publisherId, string keyId, CancellationToken cancellationToken = default)
    {
        TrustedPublisherKey? key = await context.TrustedPublisherKeys.AsNoTracking().SingleOrDefaultAsync(x => x.PublisherId == publisherId && x.KeyId == keyId, cancellationToken);
        return key is null ? null : new TrustedPublisherSnapshot(key.PublisherId, key.KeyId, key.PublicKeyPem,
            key.Status == TrustedPublisherKeyStatus.Active, key.IsTombstone, key.ConfigurationRevision);
    }
}
