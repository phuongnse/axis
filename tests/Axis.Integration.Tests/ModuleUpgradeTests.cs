using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class ModuleUpgradeTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private static readonly Guid _departmentId = Guid.Parse("5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c07");

    // Each test uses its own application id and name, so tests sharing the database do not see each other's tables.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly string _name = "Up" + Guid.NewGuid().ToString("N")[..20];

    private const string CodeField = """{ "name": "code", "type": "text" }""";
    private const string NameField = """{ "name": "name", "type": "text" }""";
    private const string BudgetField = """{ "name": "budget", "type": "integer" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_module_version_that_drops_a_field_is_refused_and_nothing_is_applied()
    {
        using var modules = Modules(("1.0.0", [CodeField, NameField]));
        using var folder = Application();
        Assert.Empty((await ActivateAsync(await CompileAsync(folder, modules))).Diagnostics);
        var activeBefore = await ActiveReleaseIdAsync();
        Install(modules, "1.1.0", CodeField, BudgetField);
        PinVersion(folder, "1.1.0");
        var upgrade = await CompileAsync(folder, modules);

        var result = await ActivateAsync(upgrade);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.NonAdditiveModuleUpgrade, "modules/departments/Department.json", "/fields"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'name'", diagnostic.Message, StringComparison.Ordinal);
        Assert.NotEqual(activeBefore, upgrade.Release!.Id);
        Assert.Equal(activeBefore, await ActiveReleaseIdAsync());
        var columns = await ReadColumnsAsync();
        Assert.Contains("f_name", columns);
        Assert.DoesNotContain("f_budget", columns);
    }

    [Fact]
    public async Task A_module_version_that_changes_a_field_type_is_refused()
    {
        using var modules = Modules(("1.0.0", [CodeField, NameField]));
        using var folder = Application();
        Assert.Empty((await ActivateAsync(await CompileAsync(folder, modules))).Diagnostics);
        var activeBefore = await ActiveReleaseIdAsync();
        Install(modules, "1.1.0", CodeField, """{ "name": "name", "type": "integer" }""");
        PinVersion(folder, "1.1.0");

        var result = await ActivateAsync(await CompileAsync(folder, modules));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.NonAdditiveModuleUpgrade, "modules/departments/Department.json", "/fields/1/type"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(activeBefore, await ActiveReleaseIdAsync());
    }

    [Fact]
    public async Task A_module_version_that_only_adds_a_field_activates_and_adds_the_column()
    {
        using var modules = Modules(("1.0.0", [CodeField, NameField]));
        using var folder = Application();
        Assert.Empty((await ActivateAsync(await CompileAsync(folder, modules))).Diagnostics);
        Install(modules, "1.1.0", CodeField, NameField, BudgetField);
        PinVersion(folder, "1.1.0");
        var upgrade = await CompileAsync(folder, modules);

        var result = await ActivateAsync(upgrade);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(upgrade.Release!.Id, await ActiveReleaseIdAsync());
        Assert.Contains("f_budget", await ReadColumnsAsync());
    }

    [Fact]
    public async Task Activating_the_same_release_again_passes_the_check()
    {
        using var modules = Modules(("1.0.0", [CodeField, NameField]));
        using var folder = Application();
        var compiled = await CompileAsync(folder, modules);
        Assert.Empty((await ActivateAsync(compiled)).Diagnostics);

        Assert.Empty((await ActivateAsync(compiled)).Diagnostics);
    }

    private static void Install(TemporaryFolder modules, string version, params string[] fields)
    {
        modules.With($"departments/{version}/module.json", $$"""{ "kind": "module", "id": "departments", "version": "{{version}}", "label": "Departments" }""");
        modules.With($"departments/{version}/Department.json", $$"""
            { "id": "{{_departmentId}}", "kind": "entity", "name": "Department", "formatVersion": 1,
              "fields": [ {{string.Join(", ", fields)}} ] }
            """);
    }

    private static TemporaryFolder Modules(params (string Version, string[] Fields)[] versions)
    {
        var root = new TemporaryFolder();
        foreach (var (version, fields) in versions)
        {
            Install(root, version, fields);
        }

        return root;
    }

    private TemporaryFolder Application(string version = "1.0.0") =>
        new TemporaryFolder().With("application.json", ApplicationJson(version));

    private void PinVersion(TemporaryFolder folder, string version) =>
        folder.With("application.json", ApplicationJson(version));

    private string ApplicationJson(string version) => $$"""
        { "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1,
          "modules": [{ "id": "departments", "version": "{{version}}" }] }
        """;

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder, TemporaryFolder modules)
    {
        var catalog = ModuleCatalog.Load(modules.Path);
        Assert.Empty(catalog.Diagnostics);
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, [], catalog.Modules, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        return compiled;
    }

    // One connection for the store and the data context, as activation needs.
    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        return await ReleaseActivator.ActivateAsync(
            compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken);
    }

    private async Task<Guid?> ActiveReleaseIdAsync()
    {
        await using var configuration = database.CreateConfigurationContext();
        return (await new ActiveReleaseStore(configuration).FindByApplicationIdAsync(_applicationId, CancellationToken))?.ReleaseId;
    }

    private async Task<List<string>> ReadColumnsAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = @schema AND table_name = @table");
        command.Parameters.AddWithValue("schema", EntityNaming.Schema);
        command.Parameters.AddWithValue("table", EntityNaming.Table(ModuleResourceIds.Derive(_applicationId, _departmentId)));
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var columns = new List<string>();
        while (await reader.ReadAsync(CancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }
}
