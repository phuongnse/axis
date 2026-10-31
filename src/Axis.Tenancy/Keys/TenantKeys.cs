using System.Collections.Concurrent;

namespace Axis.Tenancy.Keys;

/// <summary>
/// Gives the server and the worker the keys of a tenant: the newest key of a kind for a write, a key by number for a read and every key of a kind for a lookup.
/// It reads a tenant's keys from the <see cref="ITenantKeySource"/> when first needed, and again when asked for a number it
/// does not hold. It writes no log, and no message holds a key.
/// </summary>
public sealed class TenantKeys(ITenantKeySource source)
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<TenantKey>> _cache = new(StringComparer.Ordinal);

    /// <summary>Returns the key of the kind with the highest number, or <see langword="null"/> when the tenant has none of that kind.</summary>
    public async Task<TenantKey?> NewestAsync(string tenantId, TenantKeyKind kind, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        if (!_cache.TryGetValue(tenantId, out var keys))
        {
            keys = await LoadAsync(tenantId, cancellationToken);
        }

        return keys.Where(key => key.Kind == kind).MaxBy(key => key.Number);
    }

    /// <summary>Returns every key of the kind, ordered by number. It is empty when the tenant has none of that kind.</summary>
    public async Task<IReadOnlyList<TenantKey>> AllAsync(string tenantId, TenantKeyKind kind, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        if (!_cache.TryGetValue(tenantId, out var keys))
        {
            keys = await LoadAsync(tenantId, cancellationToken);
        }

        return [.. keys.Where(key => key.Kind == kind).OrderBy(key => key.Number)];
    }

    /// <summary>
    /// Returns the key of the kind with the number. When the tenant's keys held in memory lack it, they are read again once.
    /// Returns <see langword="null"/> when it is still missing.
    /// </summary>
    public async Task<TenantKey?> FindAsync(string tenantId, TenantKeyKind kind, int number, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        if (_cache.TryGetValue(tenantId, out var keys) && Find(keys, kind, number) is { } cached)
        {
            return cached;
        }

        return Find(await LoadAsync(tenantId, cancellationToken), kind, number);
    }

    private async Task<IReadOnlyList<TenantKey>> LoadAsync(string tenantId, CancellationToken cancellationToken)
    {
        var keys = await source.ReadAsync(tenantId, cancellationToken);
        _cache[tenantId] = keys;
        return keys;
    }

    private static TenantKey? Find(IReadOnlyList<TenantKey> keys, TenantKeyKind kind, int number) =>
        keys.FirstOrDefault(key => key.Kind == kind && key.Number == number);
}
