using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.LoadData;

namespace Axis.Configuration.Tests;

public sealed class LoadDataTests
{
    [Fact]
    public void Generated_application_compiles_cleanly_with_a_typical_size()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "app");

        LoadDataGenerator.Write(path, seed: 1);
        var result = ApplicationCompiler.Compile(path);

        Assert.Empty(result.Diagnostics);
        var model = result.Model;
        Assert.NotNull(model);
        Assert.InRange(model.Entities.Count, 30, 90);
        Assert.InRange(CountDeclaredFields(path), 800, 2_000);
        Assert.InRange(model.DataSources.Count, 150, 250);
        Assert.InRange(Directory.GetFiles(Path.Combine(path, "rules"), "*.json").Length, 250, 300);
        Assert.InRange(model.Processes.Count, 400, 700);
        Assert.InRange(model.Pages.Count, 60, 130);
        Assert.InRange(model.Texts.Count, 2, 3);
        Assert.All(model.Texts, texts => Assert.InRange(texts.Texts.Count, 1_500, 3_000));

        var endpoint = Assert.Single(model.Endpoints);
        Assert.Equal("CreateEntity00", endpoint.Name);
        var callerPolicy = Assert.Single(model.Policies, policy => policy.Roles.Contains("load-caller"));
        var grant = Assert.Single(callerPolicy.Grants);
        Assert.Equal("CreateEntity00", grant.Endpoint?.Name);
        Assert.Equal([PolicyAction.Invoke], grant.Actions);
    }

    [Fact]
    public void Generated_application_calls_a_connector_and_delivers_an_event_from_Entity00_updates()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "app");

        LoadDataGenerator.Write(path, seed: 1);
        var result = ApplicationCompiler.Compile(path);

        Assert.Empty(result.Diagnostics);
        var model = result.Model;
        Assert.NotNull(model);

        var connector = Assert.Single(model.Connectors);
        Assert.Equal("LoadService", connector.Name);
        Assert.Equal(LoadDataGenerator.ConnectorUrlBinding, connector.BaseUrlBinding);
        Assert.Equal("LoadServiceUrl", connector.BaseUrlBinding);
        Assert.Single(connector.Operations);

        var dataChange = Assert.Single(model.Triggers, trigger => trigger.Type == TriggerType.DataChange);
        Assert.Equal("Entity00", dataChange.Entity?.Name);
        Assert.Equal(DataChangeKinds.Update, dataChange.On);
        Assert.Equal(["quantity"], dataChange.Fields);
        var announce = Assert.Single(model.Processes, process => process.Name == dataChange.Process.Name);
        var raise = Assert.Single(announce.Steps.OfType<RaiseEventStepModel>());
        Assert.Equal("Entity00Changed", raise.Event);

        var delivered = Assert.Single(model.Triggers, trigger => trigger.Type == TriggerType.Event);
        Assert.Equal(TriggerMode.Async, delivered.Mode);
        Assert.Equal("Entity00Changed", delivered.Event?.Name);
        var notify = Assert.Single(model.Processes, process => process.Name == delivered.Process.Name);
        var call = Assert.Single(notify.Steps.OfType<CallConnectorStepModel>());
        Assert.Equal("LoadService", call.Connector);
    }

    [Fact]
    public void Same_seed_writes_identical_files_and_another_seed_keeps_the_shape()
    {
        using var folder = new TemporaryFolder();
        var first = Path.Combine(folder.Path, "first");
        var second = Path.Combine(folder.Path, "second");
        var other = Path.Combine(folder.Path, "other");

        LoadDataGenerator.Write(first, seed: 7);
        LoadDataGenerator.Write(second, seed: 7);
        LoadDataGenerator.Write(other, seed: 8);

        var paths = RelativePaths(first);
        Assert.Equal(paths, RelativePaths(second));
        Assert.Equal(paths, RelativePaths(other));
        foreach (var relative in paths)
        {
            Assert.True(
                File.ReadAllBytes(Path.Combine(first, relative)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(second, relative))),
                $"{relative} differs between two runs with the same seed.");
        }

        Assert.NotEqual(File.ReadAllText(Path.Combine(first, "application.json")), File.ReadAllText(Path.Combine(other, "application.json")));
    }

    [Fact]
    public void Every_entity_has_one_seed_file_of_100_records()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "app");
        LoadDataGenerator.Write(path, seed: 3);
        var model = ApplicationCompiler.Compile(path).Model;
        Assert.NotNull(model);

        var seeded = Directory.GetFiles(Path.Combine(path, "seeds"), "*.json").Select(file =>
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            return (
                Entity: document.RootElement.GetProperty("entity").GetString(),
                Records: document.RootElement.GetProperty("records").GetArrayLength());
        }).ToList();

        Assert.Equal(model.Entities.Select(entity => entity.Name).Order(StringComparer.Ordinal), seeded.Select(seed => seed.Entity).Order(StringComparer.Ordinal));
        Assert.All(seeded, seed => Assert.Equal(LoadDataGenerator.RecordsPerEntity, seed.Records));
    }

    [Fact]
    public void A_folder_that_holds_files_is_refused()
    {
        using var folder = new TemporaryFolder().With("old.json", "{}");

        Assert.Throws<InvalidOperationException>(() => LoadDataGenerator.Write(folder.Path, seed: 1));
        Assert.Equal(["old.json"], RelativePaths(folder.Path));
    }

    private static int CountDeclaredFields(string path) =>
        Directory.GetFiles(Path.Combine(path, "entities"), "*.json").Sum(file =>
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            return document.RootElement.GetProperty("fields").GetArrayLength();
        });

    private static List<string> RelativePaths(string root) =>
        [.. Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(root, file)).Order(StringComparer.Ordinal)];
}
