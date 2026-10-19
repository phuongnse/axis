using Axis.Tenancy;
using Axis.Tenancy.Platform;

namespace Axis.Worker;

/// <summary>
/// The tenant catalog of the worker. Under the <c>configuration</c> source it is loaded from the start. Under
/// <c>directory</c> it is empty until <see cref="LoadAsync"/> reads the tenant directory once, so the worker can
/// start before the platform database is ready and retry the first read. Then <see cref="RefreshAsync"/> reads it
/// again, and keeps the last good read when a read fails.
/// </summary>
internal sealed class WorkerTenantCatalog : ITenantCatalog
{
    private readonly string? _platformConnectionString;
    private readonly TenantSecretCipher? _cipher;
    private volatile ITenantCatalog? _current;
    private volatile DirectoryTenantCatalog? _directory;
    private volatile bool _refreshFailed;

    private WorkerTenantCatalog(
        ITenantCatalog? catalog,
        string? platformConnectionString,
        TenantSecretCipher? cipher,
        TimeSpan refreshInterval,
        bool tenantsSectionIgnored)
    {
        _current = catalog;
        _platformConnectionString = platformConnectionString;
        _cipher = cipher;
        RefreshInterval = refreshInterval;
        FromDirectorySource = platformConnectionString is not null;
        TenantsSectionIgnored = tenantsSectionIgnored;
    }

    public IReadOnlyList<TenantEntry> Tenants => _current?.Tenants ?? [];

    /// <summary>Whether the tenants come from the tenant directory.</summary>
    public bool FromDirectorySource { get; }

    /// <summary>Whether a <c>Tenants</c> section is present although the tenants come from the tenant directory.</summary>
    public bool TenantsSectionIgnored { get; }

    /// <summary>How often the tenant directory is read again. It is not used under the <c>configuration</c> source.</summary>
    public TimeSpan RefreshInterval { get; }

    /// <summary>Whether the tenants were read. It never turns back to <c>false</c>.</summary>
    public bool IsLoaded => _current is not null;

    /// <summary>Whether the last read of the tenant directory after the first one failed. The last good read stays in use.</summary>
    public bool RefreshFailed => _refreshFailed;

    public TenantEntry? Find(string tenantId) => _current?.Find(tenantId);

    public TenantEntry? FindByHost(string host) => _current?.FindByHost(host);

    public bool IsUnavailableHost(string host) => _current?.IsUnavailableHost(host) ?? false;

    /// <summary>A catalog that is loaded from the start, such as the one of the <c>configuration</c> source.</summary>
    public static WorkerTenantCatalog Loaded(ITenantCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return new WorkerTenantCatalog(catalog, null, null, TimeSpan.Zero, tenantsSectionIgnored: false);
    }

    /// <summary>A catalog that has no tenants until <see cref="LoadAsync"/> succeeds.</summary>
    public static WorkerTenantCatalog FromDirectory(
        string platformConnectionString,
        TenantSecretCipher cipher,
        TimeSpan refreshInterval,
        bool tenantsSectionIgnored)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformConnectionString);
        ArgumentNullException.ThrowIfNull(cipher);
        return new WorkerTenantCatalog(null, platformConnectionString, cipher, refreshInterval, tenantsSectionIgnored);
    }

    /// <summary>Reads the active tenants from the tenant directory and keeps them.</summary>
    /// <exception cref="InvalidOperationException">The directory cannot be read. The message holds no connection value.</exception>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_platformConnectionString is null || _cipher is null)
        {
            return;
        }

        var loaded = await DirectoryTenantCatalog.LoadAsync(_platformConnectionString, _cipher, cancellationToken);
        _directory = loaded;
        _current = loaded;
    }

    /// <summary>
    /// Reads the tenant directory again and swaps it in. A tenant that is new, active again or on other database settings
    /// than it was served on is kept only when <paramref name="passesRoleCheck"/> accepts it. When the read fails, the last
    /// good read stays in use and <see cref="RefreshFailed"/> turns <c>true</c> until a read works.
    /// </summary>
    /// <returns>The tenants that changed or were dropped, whose connection pool must go, and the counts for the log.</returns>
    /// <exception cref="InvalidOperationException">The directory cannot be read. The message holds no connection value.</exception>
    public async Task<TenantRefreshResult> RefreshAsync(
        Func<TenantEntry, CancellationToken, Task<bool>> passesRoleCheck,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passesRoleCheck);
        if (_platformConnectionString is null || _cipher is null || _directory is not { } current)
        {
            return new TenantRefreshResult([], Tenants.Count, 0, 0);
        }

        DirectoryTenantCatalog loaded;
        try
        {
            loaded = await DirectoryTenantCatalog.LoadAsync(_platformConnectionString, _cipher, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            _refreshFailed = true;
            throw;
        }

        var pending = loaded.DatabaseChanges(current);
        var withheld = new List<string>();
        foreach (var id in pending)
        {
            if (!await passesRoleCheck(loaded.Find(id)!, cancellationToken))
            {
                withheld.Add(id);
            }
        }

        var next = loaded.Withhold(withheld);
        var changed = next.DatabaseChanges(current);
        var dropped = current.Tenants.Select(tenant => tenant.Id).Except(next.Tenants.Select(tenant => tenant.Id), StringComparer.Ordinal);
        _directory = next;
        _current = next;
        _refreshFailed = false;
        return new TenantRefreshResult([.. changed.Concat(dropped).Distinct(StringComparer.Ordinal)], next.Tenants.Count, pending.Count, withheld.Count);
    }

    /// <summary>
    /// Reads the recorded schema version of every active tenant, keyed by tenant id. It returns null under the
    /// <c>configuration</c> source, which has no directory and no recorded version.
    /// </summary>
    /// <exception cref="InvalidOperationException">The directory cannot be read. The message holds no connection value.</exception>
    public async Task<IReadOnlyDictionary<string, string>?> ReadSchemaVersionsAsync(CancellationToken cancellationToken) =>
        _platformConnectionString is null
            ? null
            : await DirectoryTenantCatalog.ReadSchemaVersionsAsync(_platformConnectionString, cancellationToken);
}

/// <param name="Retired">The tenants whose database settings changed or that are no longer active, whose old pool must go.</param>
/// <param name="TenantCount">The tenants that are active after the read.</param>
/// <param name="CheckedCount">The tenants that went through the runtime role check.</param>
/// <param name="WithheldCount">The tenants that failed the runtime role check.</param>
internal sealed record TenantRefreshResult(IReadOnlyList<string> Retired, int TenantCount, int CheckedCount, int WithheldCount);
