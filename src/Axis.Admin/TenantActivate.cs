using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Data;
using Axis.Tenancy.Platform;

namespace Axis.Admin;

/// <summary>
/// <c>activate</c>: compiles an application folder and activates its release for each tenant, on its own. For each tenant
/// it checks the schema version, compiles, activates and seeds. A tenant stops at its first failed step and keeps its
/// previous active release, and the other tenants go on. Each step writes one <c>ok</c> line, or one <c>failed</c> line for
/// each error diagnostic. A line names the tenant and never a password, a host or a connection string. Compile and
/// activate run as the tenant's schema role, over the host and port that the directory records for it. The schedule
/// trigger state and the seed records run as its runtime role. Warnings go to the error writer.
/// </summary>
internal sealed class TenantActivate(
    PlatformSettings settings,
    IReadOnlyList<TenantRow> tenants,
    IReadOnlyDictionary<string, ActivationBindings> bindings,
    string folder,
    PackageCatalog packages,
    ModuleCatalog modules,
    int inlineRecomputeLimit,
    TextWriter output,
    TextWriter error,
    CancellationToken cancellationToken)
{
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
        await using var steps = new TenantDatabaseSteps(settings.Cipher, row, row.DatabaseHost, row.DatabasePort, cancellationToken);
        var run = await new TenantReleaseActivation(inlineRecomputeLimit, output, error, cancellationToken).RunAsync(
            row,
            steps,
            bindings[row.Id],
            configuration => ReleaseCompiler.CompileAsync(folder, configuration, packages.Versions, modules.Modules, cancellationToken),
            []);
        return run.Succeeded;
    }
}
