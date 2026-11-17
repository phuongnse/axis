using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The check of a registered package manifest against the releases in use, see
/// docs/reference/extension-packages.md#customer-hosted-runners. Each test has its own database, because the releases in
/// use span the whole database.
/// </summary>
public sealed class RegisteredManifestCheckTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly string _name = $"Registered{Guid.NewGuid():N}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_manifest_equal_to_the_customer_hosted_package_of_the_active_release_is_equal_and_other_operations_are_different()
    {
        var connectionString = await CreateTenantDatabaseAsync();
        await ActivateOwnAsync(connectionString, ToolsManifest("1.0.0", "sum"));

        var equal = await CheckAsync(connectionString, Parse(ToolsManifest("1.0.0", "sum")));
        var spaced = await CheckAsync(connectionString, Parse(ToolsManifest("1.0.0", "sum").Replace("\n", "\n\n", StringComparison.Ordinal)));
        var other = await CheckAsync(connectionString, Parse(ToolsManifest("1.0.0", "multiply")));

        Assert.Equal(RegisteredManifestOutcome.Equal, equal);
        Assert.Equal(RegisteredManifestOutcome.Equal, spaced);
        Assert.Equal(RegisteredManifestOutcome.Different, other);
    }

    [Fact]
    public async Task Only_the_id_the_version_and_the_operations_are_compared()
    {
        var connectionString = await CreateTenantDatabaseAsync();
        await ActivateOwnAsync(connectionString, ToolsManifest("1.0.0", "sum"));
        var registered = JsonNode.Parse(ToolsManifest("1.0.0", "sum"))!.AsObject();
        registered["label"] = "Another label";
        registered["lock"] = "another.txt";

        Assert.Equal(RegisteredManifestOutcome.Equal, await CheckAsync(connectionString, Parse(registered.ToJsonString())));

        registered.Remove("operations");
        Assert.Equal(RegisteredManifestOutcome.Different, await CheckAsync(connectionString, Parse(registered.ToJsonString())));
    }

    [Fact]
    public async Task A_version_that_no_release_in_use_pins_is_not_pinned()
    {
        var connectionString = await CreateTenantDatabaseAsync();
        await ActivateOwnAsync(connectionString, ToolsManifest("1.0.0", "sum"));

        Assert.Equal(RegisteredManifestOutcome.NotPinned, await CheckAsync(connectionString, Parse(ToolsManifest("1.0.1", "sum"))));
        Assert.Equal(RegisteredManifestOutcome.NotPinned, await CheckAsync(connectionString, Parse(ToolsManifest("1.0.0", "sum").Replace("\"tools\"", "\"other\"", StringComparison.Ordinal))));
        Assert.Equal(RegisteredManifestOutcome.NotPinned, await CheckAsync(connectionString, Parse("""{ "label": "No id or version" }""")));
    }

    [Fact]
    public async Task An_installed_package_pinned_without_the_customer_runner_is_not_pinned()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        var installed = PackageCatalog.Load(packages.Path);
        var connectionString = await CreateTenantDatabaseAsync();
        await ActivatePinnedAsync(connectionString, installed, null);

        var outcome = await CheckAsync(connectionString, ParseFile(packages, TestPackages.Id, "1.0.0"), installed);

        Assert.Equal(RegisteredManifestOutcome.NotPinned, outcome);
    }

    [Fact]
    public async Task A_release_in_use_only_through_a_waiting_instance_counts_and_its_pin_is_compared()
    {
        var connectionString = await CreateTenantDatabaseAsync();
        var first = await ActivateOwnAsync(connectionString, ToolsManifest("1.0.0", "sum"));
        await ActivateOwnAsync(connectionString, ToolsManifest("2.0.0", "sum"));
        await InsertInstanceAsync(connectionString, first, "waiting");

        Assert.Equal(RegisteredManifestOutcome.Equal, await CheckAsync(connectionString, Parse(ToolsManifest("1.0.0", "sum"))));
        Assert.Equal(RegisteredManifestOutcome.Different, await CheckAsync(connectionString, Parse(ToolsManifest("1.0.0", "multiply"))));
    }

    [Fact]
    public async Task A_release_with_a_running_instance_counts_in_the_same_way()
    {
        var connectionString = await CreateTenantDatabaseAsync();
        var first = await ActivateOwnAsync(connectionString, ToolsManifest("1.0.0", "sum"));
        await ActivateOwnAsync(connectionString, ToolsManifest("2.0.0", "sum"));
        await InsertInstanceAsync(connectionString, first, "running");

        Assert.Equal(RegisteredManifestOutcome.Equal, await CheckAsync(connectionString, Parse(ToolsManifest("1.0.0", "sum"))));
    }

    [Fact]
    public async Task A_release_that_is_not_active_and_has_only_an_ended_instance_does_not_count()
    {
        var connectionString = await CreateTenantDatabaseAsync();
        var first = await ActivateOwnAsync(connectionString, ToolsManifest("1.0.0", "sum"));
        await ActivateOwnAsync(connectionString, ToolsManifest("2.0.0", "sum"));
        await InsertInstanceAsync(connectionString, first, "completed");

        Assert.Equal(RegisteredManifestOutcome.NotPinned, await CheckAsync(connectionString, Parse(ToolsManifest("1.0.0", "sum"))));
        Assert.Equal(RegisteredManifestOutcome.Equal, await CheckAsync(connectionString, Parse(ToolsManifest("2.0.0", "sum"))));
    }

    [Fact]
    public async Task One_release_in_use_that_pins_the_version_with_other_operations_makes_it_different()
    {
        var connectionString = await CreateTenantDatabaseAsync();
        var first = await ActivateOwnAsync(connectionString, ToolsManifest("1.0.0", "sum"));
        await ActivateOwnAsync(connectionString, ToolsManifest("1.0.0", "multiply"));
        await InsertInstanceAsync(connectionString, first, "waiting");

        Assert.Equal(RegisteredManifestOutcome.Different, await CheckAsync(connectionString, Parse(ToolsManifest("1.0.0", "sum"))));
        Assert.Equal(RegisteredManifestOutcome.Different, await CheckAsync(connectionString, Parse(ToolsManifest("1.0.0", "multiply"))));
    }

    [Fact]
    public async Task An_installed_package_pinned_as_customer_hosted_is_compared_with_its_installed_manifest()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        var installed = PackageCatalog.Load(packages.Path);
        var connectionString = await CreateTenantDatabaseAsync();
        await ActivatePinnedAsync(connectionString, installed, "customer");
        var registered = ParseFile(packages, TestPackages.Id, "1.0.0");

        Assert.Equal(RegisteredManifestOutcome.Equal, await CheckAsync(connectionString, registered, installed));

        var changed = JsonNode.Parse(registered.GetRawText())!.AsObject();
        changed["operations"]!.AsArray()[0]!["name"] = "renamed";
        Assert.Equal(RegisteredManifestOutcome.Different, await CheckAsync(connectionString, Parse(changed.ToJsonString()), installed));
        Assert.Equal(RegisteredManifestOutcome.NotPinned, await CheckAsync(connectionString, Parse(registered.GetRawText().Replace("\"1.0.0\"", "\"1.1.0\"", StringComparison.Ordinal)), installed));
    }

    [Fact]
    public async Task An_installed_customer_hosted_pin_whose_manifest_file_is_gone_is_different()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        var installed = PackageCatalog.Load(packages.Path);
        var connectionString = await CreateTenantDatabaseAsync();
        await ActivatePinnedAsync(connectionString, installed, "customer");
        var registered = ParseFile(packages, TestPackages.Id, "1.0.0");
        File.Delete(Path.Combine(packages.Path, TestPackages.Id, "1.0.0", PackageCatalog.ManifestFileName));

        Assert.Equal(RegisteredManifestOutcome.Different, await CheckAsync(connectionString, registered, installed));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement ParseFile(TemporaryFolder packages, string id, string version) =>
        Parse(File.ReadAllText(Path.Combine(packages.Path, id, version, PackageCatalog.ManifestFileName)));

    private static string ToolsManifest(string version, string operation) =>
        $$"""
        {
          "id": "tools",
          "version": "{{version}}",
          "label": "Tools",
          "language": "python",
          "runner": "customer",
          "adapter": "adapter.py",
          "lock": "requirements.txt",
          "operations": [
            {
              "name": "{{operation}}",
              "type": "adapter.{{operation}}",
              "idempotent": true,
              "input": [
                { "name": "a", "type": "integer", "required": true },
                { "name": "b", "type": "integer", "required": true }
              ],
              "output": [{ "name": "total", "type": "integer" }]
            }
          ]
        }
        """;

    /// <summary>Creates a database with the tables of a tenant and returns its connection string.</summary>
    private async Task<string> CreateTenantDatabaseAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("registered", CancellationToken);
        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options);
        await using var data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connectionString).Options);
        await using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options);
        await configuration.Database.MigrateAsync(CancellationToken);
        await data.Database.MigrateAsync(CancellationToken);
        await processes.Database.MigrateAsync(CancellationToken);
        await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString);
        return connectionString;
    }

    /// <summary>Compiles and activates the application with the own package files, and no installed package pinned.</summary>
    private Task<Guid> ActivateOwnAsync(string connectionString, string manifest) =>
        ActivateAsync(connectionString, [], "[]", ("packages/tools/axis-package.json", manifest));

    /// <summary>Compiles and activates the application with the installed test package pinned, <paramref name="runner"/> being the pin's <c>runner</c>.</summary>
    private Task<Guid> ActivatePinnedAsync(string connectionString, PackageCatalog installed, string? runner)
    {
        var pin = runner is null
            ? $$"""{ "id": "{{TestPackages.Id}}", "version": "1.0.0" }"""
            : $$"""{ "id": "{{TestPackages.Id}}", "version": "1.0.0", "runner": "{{runner}}" }""";
        return ActivateAsync(connectionString, installed.Versions, $"[{pin}]");
    }

    private async Task<Guid> ActivateAsync(
        string connectionString, IReadOnlyCollection<PackageVersion> versions, string packages, params (string Path, string Content)[] files)
    {
        var application = $$"""
            { "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1, "packages": {{packages}} }
            """;
        var resources = new List<ResourceContent> { new("application.json", application) };
        resources.AddRange(files.Select(file => new ResourceContent(file.Path, file.Content)));

        await using var context = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options);
        var compiled = await ReleaseCompiler.CompileAsync(resources, context, versions, [], CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        var releaseId = compiled.Release!.Id;
        var set = await new ActiveReleaseStore(context).TrySetAsync(_applicationId, _name, releaseId, [], DateTimeOffset.UtcNow, ActivationActor.Operator, CancellationToken);
        Assert.Equal(SetActiveReleaseResult.Set, set);
        return releaseId;
    }

    private static async Task<RegisteredManifestOutcome> CheckAsync(string connectionString, JsonElement manifest, PackageCatalog? installed = null)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(connectionString));
        await connection.OpenAsync(CancellationToken);
        return await RegisteredManifestCheck.CheckAsync(connection, manifest, installed ?? PackageCatalog.Empty, CancellationToken);
    }

    private async Task InsertInstanceAsync(string connectionString, Guid releaseId, string state)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(
            $"""
            INSERT INTO axis.process_instances (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step, ended_at)
            VALUES ('{Guid.NewGuid()}', '{_applicationId}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{releaseId}', '{state}', 1, 'start',
                {(state is ProcessStarts.Running or ProcessStarts.Waiting ? "NULL" : "now()")})
            """);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
