using Axis.Configuration.Modules;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A layer that adds a field to a module entity adds the column to the application's copy at activation, and the rows
/// written before the layer keep their values.
/// </summary>
public sealed class ModuleEntityLayerTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private static readonly Guid _departmentId = Guid.Parse("5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c31");

    // Each test uses its own application id and name, so tests sharing the database do not see each other's tables.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly string _name = "Ly" + Guid.NewGuid().ToString("N")[..20];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_layer_that_adds_a_field_adds_the_column_and_keeps_existing_rows()
    {
        using var modules = Modules();
        using var folder = Application();
        Assert.Empty((await ActivateAsync(await CompileAsync(folder, modules))).Diagnostics);
        Assert.DoesNotContain("f_costcenter", await ReadColumnsAsync());
        var id = await InsertAsync("D1", "Finance");
        folder.With("customizations/departments/Department.json", """
            { "id": "003ef76b-850e-48d1-8926-ab09c4617b59", "kind": "layer", "formatVersion": 1, "target": "departments/Department",
              "changes": { "fields": [ { "name": "costCenter", "type": "text" } ] } }
            """);

        var result = await ActivateAsync(await CompileAsync(folder, modules));

        Assert.Empty(result.Diagnostics);
        Assert.Contains("f_costcenter", await ReadColumnsAsync());
        Assert.Equal(("D1", "Finance", (string?)null), await ReadRowAsync(id));
    }

    private static TemporaryFolder Modules() =>
        new TemporaryFolder()
            .With("departments/1.0.0/module.json", """{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "Departments" }""")
            .With("departments/1.0.0/Department.json", $$"""
                { "id": "{{_departmentId}}", "kind": "entity", "name": "Department", "formatVersion": 1,
                  "fields": [ { "name": "code", "type": "text" }, { "name": "name", "type": "text" } ] }
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

    private string Table() =>
        EntityNaming.QualifiedTable(EntityNaming.Table(ModuleResourceIds.Derive(_applicationId, _departmentId)));

    private async Task<Guid> InsertAsync(string code, string name)
    {
        var id = Guid.NewGuid();
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(
            $"INSERT INTO {Table()} ({EntityNaming.Quote(EntityNaming.IdColumn)}, f_code, f_name) VALUES (@id, @code, @name)");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("name", name);
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private async Task<(string? Code, string? Name, string? CostCenter)> ReadRowAsync(Guid id)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(
            $"SELECT f_code, f_name, f_costcenter FROM {Table()} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (
            await reader.IsDBNullAsync(0, CancellationToken) ? null : reader.GetString(0),
            await reader.IsDBNullAsync(1, CancellationToken) ? null : reader.GetString(1),
            await reader.IsDBNullAsync(2, CancellationToken) ? null : reader.GetString(2));
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
