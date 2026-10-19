using Axis.Tenancy;
using Axis.Tenancy.Platform;

namespace Axis.Worker;

/// <summary>
/// The tenant catalog of the worker. Under the <c>configuration</c> source it is loaded from the start. Under
/// <c>directory</c> it is empty until <see cref="LoadAsync"/> reads the tenant directory once, so the worker can
/// start before the platform database is ready and retry the first read.
/// </summary>
internal sealed class WorkerTenantCatalog : ITenantCatalog
{
    private readonly string? _platformConnectionString;
    private readonly TenantSecretCipher? _cipher;
    private volatile ITenantCatalog? _current;

    private WorkerTenantCatalog(ITenantCatalog? catalog, string? platformConnectionString, TenantSecretCipher? cipher, bool tenantsSectionIgnored)
    {
        _current = catalog;
        _platformConnectionString = platformConnectionString;
        _cipher = cipher;
        FromDirectorySource = platformConnectionString is not null;
        TenantsSectionIgnored = tenantsSectionIgnored;
    }

    public IReadOnlyList<TenantEntry> Tenants => _current?.Tenants ?? [];

    /// <summary>Whether the tenants come from the tenant directory.</summary>
    public bool FromDirectorySource { get; }

    /// <summary>Whether a <c>Tenants</c> section is present although the tenants come from the tenant directory.</summary>
    public bool TenantsSectionIgnored { get; }

    /// <summary>Whether the tenants were read. It never turns back to <c>false</c>.</summary>
    public bool IsLoaded => _current is not null;

    public TenantEntry? Find(string tenantId) => _current?.Find(tenantId);

    public TenantEntry? FindByHost(string host) => _current?.FindByHost(host);

    public bool IsUnavailableHost(string host) => _current?.IsUnavailableHost(host) ?? false;

    /// <summary>A catalog that is loaded from the start, such as the one of the <c>configuration</c> source.</summary>
    public static WorkerTenantCatalog Loaded(ITenantCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return new WorkerTenantCatalog(catalog, null, null, tenantsSectionIgnored: false);
    }

    /// <summary>A catalog that has no tenants until <see cref="LoadAsync"/> succeeds.</summary>
    public static WorkerTenantCatalog FromDirectory(string platformConnectionString, TenantSecretCipher cipher, bool tenantsSectionIgnored)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformConnectionString);
        ArgumentNullException.ThrowIfNull(cipher);
        return new WorkerTenantCatalog(null, platformConnectionString, cipher, tenantsSectionIgnored);
    }

    /// <summary>Reads the active tenants from the tenant directory and keeps them.</summary>
    /// <exception cref="InvalidOperationException">The directory cannot be read. The message holds no connection value.</exception>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_platformConnectionString is null || _cipher is null)
        {
            return;
        }

        _current = await DirectoryTenantCatalog.LoadAsync(_platformConnectionString, _cipher, cancellationToken);
    }
}
