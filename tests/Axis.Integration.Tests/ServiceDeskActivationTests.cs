using System.Text.Json.Nodes;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The service desk sample declares no module, package or connector, so it compiles and activates
/// for a tenant that holds no deployment binding. A tenant that still holds a reopened ticket gets it
/// back as a draft through the sample's migration.
/// </summary>
public sealed class ServiceDeskActivationTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Samples", "service-desk");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static readonly Guid _ticketId = Guid.Parse("4a4372e6-8a54-4c8f-b784-b18ddbcd7770");

    [Fact]
    public async Task The_sample_activates_over_a_reopened_ticket_and_the_migration_turns_it_into_a_draft()
    {
        // The release that still lists reopened has no migration, so it is the sample as it was before the loop.
        using var before = new TemporaryFolder();
        CopyDirectory(_appFolder, before.Path);
        Directory.Delete(Path.Combine(before.Path, "migrations"), recursive: true);
        var ticketFile = Path.Combine(before.Path, "entities", "ticket.json");
        var ticket = JsonNode.Parse(File.ReadAllText(ticketFile))!;
        var status = ticket["fields"]!.AsArray().Single(field => field!["name"]!.GetValue<string>() == "status")!;
        status["values"]!.AsArray().Add("reopened");
        File.WriteAllText(ticketFile, ticket.ToJsonString());

        var first = await CompileAsync(before.Path);
        Assert.Empty(first.Diagnostics);
        Assert.Empty((await ActivateAsync(first)).Diagnostics);
        await InsertReopenedTicketAsync();

        var compiled = await CompileAsync(_appFolder);
        Assert.Empty(compiled.Diagnostics);
        var activated = await ActivateAsync(compiled);

        Assert.Empty(activated.Diagnostics);
        Assert.Equal("draft", await ReadTicketStatusAsync());
        await using var reader = database.CreateConfigurationContext();
        var active = await new ActiveReleaseStore(reader).FindByNameAsync("ServiceDesk", CancellationToken);
        Assert.NotNull(active);
        Assert.Equal(compiled.Release!.Id, active.ReleaseId);
    }

    private async Task<ReleaseCompilationResult> CompileAsync(string folder)
    {
        await using var context = database.CreateConfigurationContext();
        return await ReleaseCompiler.CompileAsync(folder, context, SamplePackages.Versions, CancellationToken);
    }

    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        return await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("acme"), OpenInstanceCounter.Instance, CancellationToken);
    }

    private async Task InsertReopenedTicketAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(
            $"INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(_ticketId))} (id, f_title, f_status) VALUES (@id, @title, @status)");
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("title", "Laptop will not start");
        command.Parameters.AddWithValue("status", "reopened");
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<string?> ReadTicketStatusAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"SELECT f_status FROM {EntityNaming.QualifiedTable(EntityNaming.Table(_ticketId))}");
        return (string?)await command.ExecuteScalarAsync(CancellationToken);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }
}
