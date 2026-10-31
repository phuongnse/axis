using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Data;
using Axis.Data.Records;
using Axis.Tenancy;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;

namespace Axis.Admin;

/// <summary>
/// <c>activate</c>: compiles an application folder and activates its release for each tenant, on its own. For each tenant
/// it checks the schema version, compiles, activates and seeds. After the activation commits, the activate step updates the
/// tenant's realm, including its mail server, unless the tenant has no activation secret, such as a preview tenant. A tenant stops at its first failed step and keeps its
/// previous active release, and the other tenants go on. Each step writes one <c>ok</c> line, or one <c>failed</c> line for
/// each error diagnostic. A line names the tenant and never a password, a host or a connection string. Compile and
/// activate run as the tenant's schema role, over the host and port that the directory records for it. The schedule
/// trigger state and the seed records run as its runtime role. Warnings go to the error writer.
/// </summary>
internal sealed class TenantActivate(
    PlatformSettings settings,
    IReadOnlyList<TenantRow> tenants,
    IReadOnlyDictionary<string, ActivationBindings> bindings,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, TenantBinding>> realmBindings,
    RealmMail mail,
    HttpClient http,
    string folder,
    PackageCatalog packages,
    ModuleCatalog modules,
    int inlineRecomputeLimit,
    TimeSpan activationTimeout,
    TenantAudit audit,
    TextWriter output,
    TextWriter error,
    CancellationToken cancellationToken)
{
    // The tenants' data keys come from the tenant directory, and the seeds of every tenant share the cache.
    private readonly SensitiveValues _sensitive = new(new TenantKeys(new DirectoryTenantKeySource(settings.PlatformConnectionString, settings.Cipher)));

    public async Task<int> RunAsync()
    {
        var failed = 0;
        foreach (var row in tenants)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return 1;
            }

            if (!await ActivateTenantAsync(row))
            {
                failed++;
            }
        }

        return failed == 0 && !cancellationToken.IsCancellationRequested ? 0 : 1;
    }

    private async Task<bool> ActivateTenantAsync(TenantRow row)
    {
        if (!await audit.WriteAsync(row.Id, output, cancellationToken))
        {
            return false;
        }

        await using var steps = new TenantDatabaseSteps(settings.Cipher, row, row.DatabaseHost, row.DatabasePort, TenantDatabaseTls.From(settings.PlatformConnectionString), cancellationToken);
        var run = await new TenantReleaseActivation(inlineRecomputeLimit, activationTimeout, _sensitive, output, error, cancellationToken).RunAsync(
            row.Id,
            row.SchemaVersion,
            steps,
            bindings[row.Id],
            configuration => ReleaseCompiler.CompileAsync(folder, configuration, packages.Versions, modules.Modules, cancellationToken),
            [],
            ActivationActor.Operator,
            syncRealm: TenantReleaseActivation.RealmSync(
                http,
                DirectoryTenantCatalog.Realm(row, settings.Cipher),
                realmBindings[row.Id],
                mail.For(DirectoryTenantCatalog.Email(row, settings.Cipher)),
                cancellationToken));
        return run.Succeeded;
    }
}
