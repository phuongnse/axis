using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The service desk sample declares no module, package or connector, so it compiles and activates
/// for a tenant that holds no deployment binding.
/// </summary>
public sealed class ServiceDeskActivationTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Samples", "service-desk");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sample_compiles_and_activates_with_no_diagnostic()
    {
        ReleaseCompilationResult compiled;
        await using (var context = database.CreateConfigurationContext())
        {
            compiled = await ReleaseCompiler.CompileAsync(_appFolder, context, SamplePackages.Versions, CancellationToken);
        }

        Assert.Empty(compiled.Diagnostics);

        ActivationResult activated;
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("acme"), OpenInstanceCounter.Instance, CancellationToken);
        }

        Assert.Empty(activated.Diagnostics);
        await using var reader = database.CreateConfigurationContext();
        var active = await new ActiveReleaseStore(reader).FindByNameAsync("ServiceDesk", CancellationToken);
        Assert.NotNull(active);
        Assert.Equal(compiled.Release!.Id, active.ReleaseId);
    }
}
