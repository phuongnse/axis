using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class ModuleLayerUpgradeTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private const string ReviewFile = "customizations/approvals/Review.json";

    // Each test uses its own application id and name, so tests sharing the database do not see each other's tables.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly string _name = "Lay" + Guid.NewGuid().ToString("N")[..20];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_newer_module_version_that_keeps_every_key_the_layer_names_activates_with_the_layer_applied()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "1.0.0", "review");
        using var folder = Application();
        Assert.Empty((await ActivateAsync(await CompileAsync(folder, modules))).Diagnostics);
        Install(modules, "1.1.0", "review", extraField: """, { "name": "note", "type": "text" }""");

        var upgrade = await CompileAsync(folder, modules);
        var result = await ActivateAsync(upgrade);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(upgrade.Release!.Id, await ActiveReleaseIdAsync());
        Assert.Equal("1.1.0", Assert.Single(upgrade.Model!.Modules).Version);
        Assert.Equal(TimeSpan.FromDays(3), DueIn(upgrade.Model!));
    }

    [Fact]
    public async Task A_newer_module_version_that_renamed_a_step_the_layer_changes_is_refused_and_the_previous_release_stays_active()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "1.0.0", "review");
        using var folder = Application();
        var first = await CompileAsync(folder, modules);
        Assert.Empty((await ActivateAsync(first)).Diagnostics);
        Install(modules, "1.1.0", "approval");

        var upgrade = await CompileAsync(folder, modules, expectClean: false);

        Assert.Null(upgrade.Release);
        var dropped = Assert.Single(upgrade.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.LayerKeyDroppedByModuleUpgrade);
        Assert.Equal((ReviewFile, "/changes/steps/0/name"), (dropped.File, dropped.Path));
        var summary = Assert.Single(upgrade.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.ModuleUpgradeBreaksLayers);
        Assert.Equal(("application.json", "/modules/0"), (summary.File, summary.Path));
        Assert.Contains("1.0.0", summary.Message, StringComparison.Ordinal);
        Assert.Contains("1.1.0", summary.Message, StringComparison.Ordinal);
        Assert.Contains(ReviewFile, summary.Message, StringComparison.Ordinal);

        Assert.Equal(first.Release!.Id, await ActiveReleaseIdAsync());
        await using var configuration = database.CreateConfigurationContext();
        var active = await new ActiveReleaseStore(configuration).GetReleaseAsync(first.Release.Id, CancellationToken);
        Assert.Equal(TimeSpan.FromDays(3), DueIn(ReleaseCompiler.BuildModel(active!)));
    }

    private static TimeSpan? DueIn(ApplicationModel model) =>
        Assert.IsType<TaskStepModel>(Assert.Single(model.Processes).Steps[0]).DueIn;

    private static void Install(TemporaryFolder modules, string version, string stepName, string extraField = "")
    {
        modules
            .With($"approvals/{version}/module.json", $$"""{ "kind": "module", "id": "approvals", "version": "{{version}}", "label": "Approvals" }""")
            .With($"approvals/{version}/Approval.json", $$"""
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c21", "kind": "entity", "name": "Approval", "formatVersion": 1,
                  "fields": [ { "name": "title", "type": "text" }{{extraField}} ] }
                """)
            .With($"approvals/{version}/forms/ApprovalForm.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c24", "kind": "form", "name": "ApprovalForm", "formatVersion": 1, "entity": "Approval",
                  "sections": [ { "name": "approval", "title": { "textKey": "approvals.approval" }, "fields": [ { "field": "title" } ] } ] }
                """)
            .With($"approvals/{version}/processes/Review.json", $$"""
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c41", "kind": "process", "name": "Review", "formatVersion": 1,
                  "entity": "Approval", "start": "{{stepName}}", "steps": [
                    { "name": "{{stepName}}", "type": "task", "label": { "textKey": "approvals.review" }, "assignee": { "user": "'u1'" }, "form": "ApprovalForm",
                      "outcomes": [ { "name": "approve", "label": { "textKey": "approvals.approve" }, "next": "done" } ] },
                    { "name": "done", "type": "end" }
                  ] }
                """)
            .With($"approvals/{version}/TextsEn.json", """
                { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6c", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "approvals.approval": "Approval", "approvals.review": "Review", "approvals.approve": "Approve" } }
                """);
    }

    private TemporaryFolder Application() =>
        new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1,
                  "modules": [{ "id": "approvals", "version": "^1.0.0" }] }
                """)
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "invoice.title": "Invoice" } }
                """)
            .With(ReviewFile, """
                { "id": "7a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c01", "kind": "layer", "formatVersion": 1, "target": "approvals/Review",
                  "changes": { "steps": [ { "name": "review", "dueIn": "P3D" } ] } }
                """);

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder, TemporaryFolder modules, bool expectClean = true)
    {
        var catalog = ModuleCatalog.Load(modules.Path);
        Assert.Empty(catalog.Diagnostics);
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, [], catalog.Modules, CancellationToken);
        if (expectClean)
        {
            Assert.Empty(compiled.Diagnostics);
        }

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
}
