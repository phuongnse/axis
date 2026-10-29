using Axis.Configuration.Realms;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// One tenant the publisher serves: its id, its schema version, its bindings and how to connect to its database.
/// <paramref name="HasPreview"/> means the tenant has a preview tenant in any state. <paramref name="Preview"/> is that tenant,
/// only when the publisher serves it. <paramref name="SyncRealm"/> brings the tenant's realm in step after an activation, or is
/// <c>null</c> when the tenant has no realm details, such as a preview tenant.
/// </summary>
internal sealed record PublisherTenant(
    string Id,
    string SchemaVersion,
    ActivationBindings Bindings,
    Func<ITenantConnections> Connect,
    bool HasPreview = false,
    PublisherTenant? Preview = null,
    Func<RealmPlan, Task>? SyncRealm = null);

/// <summary>Where the publisher learns the tenants it serves: the tenant directory or the <c>Tenants</c> configuration section.</summary>
internal interface IPublisherTenants
{
    /// <summary>How often <see cref="ReadAsync"/> runs. Zero means once in each poll round.</summary>
    TimeSpan ReadInterval { get; }

    /// <summary>A problem that stops the publisher before it serves any tenant, or <c>null</c>.</summary>
    Task<string?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>The tenants to serve now, in ordinal order of id.</summary>
    Task<IReadOnlyList<PublisherTenant>> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>The active tenants of the directory that are on this build's schema version.</summary>
internal sealed class DirectoryPublisherTenants(PlatformSettings settings, TimeSpan readInterval, HttpClient http) : IPublisherTenants
{
    public TimeSpan ReadInterval => readInterval;

    public Task<string?> CheckAsync(CancellationToken cancellationToken) =>
        AdminCommands.PlatformTablesProblemAsync(settings.PlatformConnectionString, cancellationToken);

    public async Task<IReadOnlyList<PublisherTenant>> ReadAsync(CancellationToken cancellationToken)
    {
        await using var platform = new PlatformDbContext(
            new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(settings.PlatformConnectionString).Options);
        var rows = await platform.Tenants.AsNoTracking().ToListAsync(cancellationToken);
        var served = rows
            .Where(row => row.Status == "active" && row.SchemaVersion == TenantSchemaVersion.Current)
            .OrderBy(row => row.Id, StringComparer.Ordinal)
            .ToList();
        var ids = served.Select(row => row.Id).ToList();
        var bindings = await AdminCommands.ReadBindingsAsync(platform, ids, cancellationToken);
        var realmBindings = await AdminCommands.ReadRealmBindingsAsync(platform, ids, settings.Cipher, cancellationToken);
        var records = served.ToDictionary(
            row => row.Id,
            row => new PublisherTenant(
                row.Id,
                row.SchemaVersion,
                bindings[row.Id],
                () => new TenantDatabaseSteps(settings.Cipher, row, row.DatabaseHost, row.DatabasePort, TenantDatabaseTls.From(settings.PlatformConnectionString), CancellationToken.None),
                SyncRealm: TenantReleaseActivation.RealmSync(http, DirectoryTenantCatalog.Realm(row, settings.Cipher), realmBindings[row.Id], CancellationToken.None)),
            StringComparer.Ordinal);
        var previewIds = rows
            .Where(row => row.PreviewOf is not null)
            .ToDictionary(row => row.PreviewOf!, row => row.Id, StringComparer.Ordinal);
        return
        [
            .. served.Select(row => row.PreviewOf is null
                ? records[row.Id] with
                {
                    HasPreview = previewIds.ContainsKey(row.Id),
                    Preview = previewIds.TryGetValue(row.Id, out var previewId) && records.TryGetValue(previewId, out var preview) ? preview : null,
                }
                : records[row.Id]),
        ];
    }
}

/// <summary>
/// The tenants of the <c>Tenants</c> section, as the server and the worker read them. A tenant is served once its database is
/// fully migrated, and the server migrates it, so the first poll rounds may find it not ready. The wait and the ready state
/// each write one line for the tenant. A tenant counts as migrated for good once it was found so.
/// </summary>
internal sealed class ConfigurationPublisherTenants(
    IReadOnlyList<TenantEntry> entries,
    bool allowHttp,
    TextWriter output,
    HttpClient http) : IPublisherTenants
{
    private readonly HashSet<string> _waiting = new(StringComparer.Ordinal);
    private readonly HashSet<string> _migrated = new(StringComparer.Ordinal);
    private readonly List<PublisherTenant> _served = [];

    public TimeSpan ReadInterval => TimeSpan.Zero;

    public Task<string?> CheckAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    public async Task<IReadOnlyList<PublisherTenant>> ReadAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in entries.Where(entry => !_migrated.Contains(entry.Id)))
        {
            if (!await IsMigratedAsync(entry, cancellationToken))
            {
                if (_waiting.Add(entry.Id))
                {
                    output.WriteLine($"{entry.Id} waiting: the database is not migrated yet.");
                }

                continue;
            }

            output.WriteLine($"{entry.Id} ready");
            _migrated.Add(entry.Id);
            _served.Add(new PublisherTenant(
                entry.Id,
                TenantSchemaVersion.Current,
                Bindings(entry),
                () => new ConfiguredTenantConnections(entry),
                SyncRealm: TenantReleaseActivation.RealmSync(http, entry.Realm, entry.Bindings, CancellationToken.None)));
        }

        var previewIds = entries
            .Where(entry => entry.PreviewOf is not null)
            .ToDictionary(entry => entry.PreviewOf!, entry => entry.Id, StringComparer.Ordinal);
        var served = _served.ToDictionary(tenant => tenant.Id, StringComparer.Ordinal);
        return
        [
            .. _served
                .OrderBy(tenant => tenant.Id, StringComparer.Ordinal)
                .Select(tenant => tenant.Bindings.IsPreview
                    ? tenant
                    : tenant with
                    {
                        HasPreview = previewIds.ContainsKey(tenant.Id),
                        Preview = previewIds.TryGetValue(tenant.Id, out var previewId) && served.TryGetValue(previewId, out var preview) ? preview : null,
                    }),
        ];
    }

    // Mirrors the server's TenantActivationBindings, which this host does not reference.
    private ActivationBindings Bindings(TenantEntry tenant)
    {
        var bindings = new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, binding) in tenant.Bindings)
        {
            bindings[name] = new ActivationBinding(
                binding.Kind,
                binding.Kind == DeploymentBindingKinds.Url ? new Uri(binding.Url!, UriKind.Absolute) : null);
        }

        return new ActivationBindings(tenant.Id, bindings, AllowHttp: allowHttp, IsPreview: tenant.PreviewOf is not null)
        {
            PlatformProviders = tenant.PlatformProviders ?? [],
        };
    }

    // Every context must be fully migrated, because the server migrates them one after the other. A database that cannot be
    // reached yet counts as not migrated.
    private static async Task<bool> IsMigratedAsync(TenantEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await using var connections = new ConfiguredTenantConnections(entry);
            var connection = await connections.SchemaConnectionAsync();
            await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
            await using var data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connection).Options);
            await using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connection).Options);
            return !(await configuration.Database.GetPendingMigrationsAsync(cancellationToken)).Any()
                && !(await data.Database.GetPendingMigrationsAsync(cancellationToken)).Any()
                && !(await processes.Database.GetPendingMigrationsAsync(cancellationToken)).Any();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
