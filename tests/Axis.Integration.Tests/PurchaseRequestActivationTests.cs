using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The purchase request sample calls the purchasing system through a connector and signs people in
/// through the <c>company</c> identity provider, so it activates only for a tenant that holds the
/// connector's two deployment bindings and the provider's three.
/// </summary>
public sealed class PurchaseRequestActivationTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sample_activates_only_for_a_tenant_with_the_purchasing_and_company_bindings()
    {
        ReleaseCompilationResult compiled;
        await using (var context = database.CreateConfigurationContext())
        {
            compiled = await ReleaseCompiler.CompileAsync(_appFolder, context, SamplePackages.Versions, CancellationToken);
        }

        Assert.Empty(compiled.Diagnostics);

        var refused = await ActivateAsync(compiled, ActivationBindings.None("acme"));

        (string Code, string File, string Path, string Message)[] companyDiagnostics =
        [
            (DiagnosticCodes.MissingDeploymentBinding, "identity-providers/company.json", "/clientId/binding", "Tenant 'acme' has no binding 'CompanyClientId'. Add a 'secret' binding with that name to the tenant."),
            (DiagnosticCodes.MissingDeploymentBinding, "identity-providers/company.json", "/clientSecret/binding", "Tenant 'acme' has no binding 'CompanyClientSecret'. Add a 'secret' binding with that name to the tenant."),
            (DiagnosticCodes.MissingDeploymentBinding, "identity-providers/company.json", "/issuer/binding", "Tenant 'acme' has no binding 'CompanyIssuer'. Add a 'url' binding with that name to the tenant."),
        ];
        Assert.Equal(
            [
                (DiagnosticCodes.MissingDeploymentBinding, "connectors/purchasing-system.json", "/auth/token/binding", "Tenant 'acme' has no binding 'PurchasingSystemToken'. Add a 'secret' binding with that name to the tenant."),
                (DiagnosticCodes.MissingDeploymentBinding, "connectors/purchasing-system.json", "/baseUrl/binding", "Tenant 'acme' has no binding 'PurchasingSystemUrl'. Add a 'url' binding with that name to the tenant."),
                .. companyDiagnostics,
            ],
            refused.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.Message)).Order());
        Assert.Null(await FindActiveAsync());

        var bindings = new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase)
        {
            ["PurchasingSystemUrl"] = new(ActivationBinding.UrlKind, new Uri("https://purchasing.example.test")),
            ["PurchasingSystemToken"] = new(ActivationBinding.SecretKind, null),
        };

        var withoutCompany = await ActivateAsync(compiled, new ActivationBindings("acme", bindings, AllowHttp: false));

        Assert.Equal(
            companyDiagnostics,
            withoutCompany.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.Message)).Order());
        Assert.Null(await FindActiveAsync());

        CompanyBindings.AddTo(bindings);

        var activated = await ActivateAsync(compiled, new ActivationBindings("acme", bindings, AllowHttp: false));

        Assert.Empty(activated.Diagnostics);
        var active = await FindActiveAsync();
        Assert.NotNull(active);
        Assert.Equal(compiled.Release!.Id, active.ReleaseId);
    }

    [Fact]
    public async Task The_sample_does_not_compile_without_its_package()
    {
        // The tests of this class share one database, so the sample may already be active.
        var activeBefore = await FindActiveAsync();
        ReleaseCompilationResult compiled;
        await using (var context = database.CreateConfigurationContext())
        {
            compiled = await ReleaseCompiler.CompileAsync(_appFolder, context, [], CancellationToken);
        }

        Assert.Null(compiled.Release);
        Assert.Null(compiled.Model);
        var diagnostic = Assert.Single(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.UnknownPackage);
        Assert.Equal("application.json", diagnostic.File);
        Assert.Equal("/packages/0/id", diagnostic.Path);
        Assert.Contains("payment-references", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(activeBefore, await FindActiveAsync());
    }

    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled, ActivationBindings bindings)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        return await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, bindings, OpenInstanceCounter.Instance, CancellationToken);
    }

    /// <summary>The sample's active release, read through a fresh store.</summary>
    private async Task<ActiveRelease?> FindActiveAsync()
    {
        await using var context = database.CreateConfigurationContext();
        return await new ActiveReleaseStore(context).FindByNameAsync("PurchaseRequests", CancellationToken);
    }
}
