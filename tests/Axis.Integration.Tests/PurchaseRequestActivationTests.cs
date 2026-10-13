using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;

namespace Axis.Integration.Tests;

/// <summary>
/// The purchase request sample calls the purchasing system through a connector, so it activates
/// only for a tenant that holds the connector's two deployment bindings.
/// </summary>
public sealed class PurchaseRequestActivationTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sample_activates_only_for_a_tenant_with_both_purchasing_bindings()
    {
        ReleaseCompilationResult compiled;
        await using (var context = database.CreateConfigurationContext())
        {
            compiled = await ReleaseCompiler.CompileAsync(_appFolder, context, CancellationToken);
        }

        Assert.Empty(compiled.Diagnostics);

        var refused = await ActivateAsync(compiled, ActivationBindings.None("acme"));

        Assert.Equal(
            [
                (DiagnosticCodes.MissingDeploymentBinding, "connectors/purchasing-system.json", "/auth/token/binding", "Tenant 'acme' has no binding 'PurchasingSystemToken'. Add a 'secret' binding with that name to the tenant."),
                (DiagnosticCodes.MissingDeploymentBinding, "connectors/purchasing-system.json", "/baseUrl/binding", "Tenant 'acme' has no binding 'PurchasingSystemUrl'. Add a 'url' binding with that name to the tenant."),
            ],
            refused.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.Message)).Order());
        Assert.Null(await FindActiveAsync());

        var bindings = new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase)
        {
            ["PurchasingSystemUrl"] = new(ActivationBinding.UrlKind, new Uri("https://purchasing.example.test")),
            ["PurchasingSystemToken"] = new(ActivationBinding.SecretKind, null),
        };

        var activated = await ActivateAsync(compiled, new ActivationBindings("acme", bindings, AllowHttp: false));

        Assert.Empty(activated.Diagnostics);
        var active = await FindActiveAsync();
        Assert.NotNull(active);
        Assert.Equal(compiled.Release!.Id, active.ReleaseId);
    }

    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled, ActivationBindings bindings)
    {
        await using var configuration = database.CreateConfigurationContext();
        await using var data = database.CreateContext();
        return await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, bindings, CancellationToken);
    }

    /// <summary>The sample's active release, read through a fresh store.</summary>
    private async Task<ActiveRelease?> FindActiveAsync()
    {
        await using var context = database.CreateConfigurationContext();
        return await new ActiveReleaseStore(context).FindByNameAsync("PurchaseRequests", CancellationToken);
    }
}
