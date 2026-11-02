using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A layer that removes a module field follows the application's migration rules. A <c>dropField</c> migration on the
/// module entity drops the column of the application's copy. Without it, activation is refused in the layer file.
/// </summary>
public sealed class ModuleLayerMigrationTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private const string LayerFile = "customizations/departments/Department.json";

    private static readonly Guid _departmentId = Guid.Parse("5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c51");

    // Each test uses its own application id and name, so tests sharing the database do not see each other's tables.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly string _name = "Ml" + Guid.NewGuid().ToString("N")[..20];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_layer_that_removes_a_module_field_with_a_drop_field_migration_drops_the_column()
    {
        using var modules = Modules();
        using var folder = Application();
        var first = await CompileAsync(folder, modules);
        Assert.Empty((await ActivateAsync(first)).Diagnostics);
        Assert.Contains("f_name", await ReadColumnsAsync());
        folder
            .With(LayerFile, RemoveNameLayer)
            .With("migrations/0001.json", """
                { "id": "2b4c6d8e-1f3a-4b5c-9d7e-0a1b2c3d4e5f", "kind": "migration", "name": "DropName", "formatVersion": 1,
                  "entity": "departments/Department", "changes": [ { "type": "dropField", "field": "name" } ] }
                """);
        var second = await CompileAsync(folder, modules);

        var result = await ActivateAsync(second);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(second.Release!.Id, await ActiveReleaseIdAsync());
        var columns = await ReadColumnsAsync();
        Assert.DoesNotContain("f_name", columns);
        Assert.Contains("f_code", columns);
    }

    [Fact]
    public async Task A_layer_that_removes_a_module_field_without_a_migration_is_refused_in_the_layer_file()
    {
        using var modules = Modules();
        using var folder = Application();
        var first = await CompileAsync(folder, modules);
        Assert.Empty((await ActivateAsync(first)).Diagnostics);
        folder.With(LayerFile, RemoveNameLayer);

        var result = await ActivateAsync(await CompileAsync(folder, modules));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.RemovedField, LayerFile, "/changes/fields/0"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(first.Release!.Id, await ActiveReleaseIdAsync());
        Assert.Contains("f_name", await ReadColumnsAsync());
    }

    private const string RemoveNameLayer = """
        { "id": "003ef76b-850e-48d1-8926-ab09c4617b59", "kind": "layer", "formatVersion": 1, "target": "departments/Department",
          "changes": { "fields": [ { "name": "name", "remove": true } ] } }
        """;

    private static TemporaryFolder Modules() =>
        new TemporaryFolder()
            .With("departments/1.0.0/module.json", """{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "Departments" }""")
            .With("departments/1.0.0/Department.json", $$"""
                { "id": "{{_departmentId}}", "kind": "entity", "name": "Department", "formatVersion": 1,
                  "fields": [ { "name": "name", "type": "text" }, { "name": "code", "type": "text" } ] }
                """);

    private TemporaryFolder Application() =>
        new TemporaryFolder().With("application.json", $$"""
            { "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1,
              "modules": [{ "id": "departments", "version": "1.0.0" }] }
            """);

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
