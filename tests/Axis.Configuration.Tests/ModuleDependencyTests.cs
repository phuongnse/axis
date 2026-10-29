using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Releases;

namespace Axis.Configuration.Tests;

public sealed class ModuleDependencyTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";
    private const string AlphaEntityId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c01";
    private const string BetaEntityId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c02";
    private const string GammaEntityId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c03";

    [Fact]
    public void A_module_that_depends_on_another_pins_both_and_copies_both_into_the_release()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "^1.0.0")));
        Install(modules, "beta", "1.0.0", BetaEntityId);
        using var folder = Application(("alpha", "^1.0.0"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [new PinnedModuleModel("alpha", "^1.0.0", "1.0.0"), new PinnedModuleModel("beta", "^1.0.0", "1.0.0")],
            result.Model!.Modules);
        Assert.Equal(
            ["application.json", "modules/alpha/Item.json", "modules/alpha/module.json", "modules/beta/Item.json", "modules/beta/module.json"],
            result.Resources.Select(resource => resource.Path));
        Assert.Equal(
            [
                ("alpha/Item", ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(AlphaEntityId))),
                ("beta/Item", ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(BetaEntityId))),
            ],
            result.Model.Entities.Select(entity => (entity.Name, entity.Id)));
    }

    [Fact]
    public void Two_modules_that_ask_for_a_module_with_ranges_no_version_meets_give_one_conflict_naming_both()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "^1.0.0")));
        Install(modules, "gamma", "1.0.0", GammaEntityId, Depends(("beta", "^2.0.0")));
        Install(modules, "beta", "1.0.0", BetaEntityId);
        Install(modules, "beta", "2.0.0", BetaEntityId);
        using var folder = Application(("alpha", "^1.0.0"), ("gamma", "^1.0.0"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleRangeConflict, "modules/alpha/module.json", "/modules/0/version"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("module 'alpha' asks for '^1.0.0'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("module 'gamma' asks for '^2.0.0'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'1.0.0', '2.0.0'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_conflict_with_the_application_is_reported_at_the_application_entry()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "^1.0.0")));
        Install(modules, "beta", "1.0.0", BetaEntityId);
        Install(modules, "beta", "2.0.0", BetaEntityId);
        using var folder = Application(("alpha", "^1.0.0"), ("beta", "^2.0.0"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleRangeConflict, "application.json", "/modules/1/version", Guid.Parse(ApplicationId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("the application asks for '^2.0.0'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("module 'alpha' asks for '^1.0.0'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_range_that_no_version_meets_keeps_the_existing_code_in_the_module_manifest()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "^3.0.0")));
        Install(modules, "beta", "1.0.0", BetaEntityId);
        using var folder = Application(("alpha", "^1.0.0"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.NoModuleVersionInRange, "modules/alpha/module.json", "/modules/0/version"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void The_highest_version_that_meets_every_range_is_pinned()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "~1.0.0")));
        foreach (var version in new[] { "1.0.0", "1.0.3", "1.2.0" })
        {
            Install(modules, "beta", version, BetaEntityId);
        }

        using var folder = Application(("beta", "^1.0.0"), ("alpha", "1.0.0"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [new PinnedModuleModel("beta", "^1.0.0", "1.0.3"), new PinnedModuleModel("alpha", "1.0.0", "1.0.0")],
            result.Model!.Modules);
    }

    [Fact]
    public void A_cycle_between_modules_gives_one_diagnostic_naming_the_cycle()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "^1.0.0")));
        Install(modules, "beta", "1.0.0", BetaEntityId, Depends(("alpha", "^1.0.0")));
        using var folder = Application(("alpha", "^1.0.0"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleDependencyCycle, "modules/beta/module.json", "/modules/0/id"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'alpha' → 'beta' → 'alpha'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_that_depends_on_itself_is_a_cycle()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("alpha", "^1.0.0")));
        using var folder = Application(("alpha", "^1.0.0"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleDependencyCycle, "modules/alpha/module.json", "/modules/0/id"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'alpha' → 'alpha'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_that_two_others_depend_on_is_pinned_and_copied_once()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "^1.0.0")));
        Install(modules, "gamma", "1.0.0", GammaEntityId, Depends(("beta", "^1.0.0")));
        Install(modules, "beta", "1.0.0", BetaEntityId);
        using var folder = Application(("alpha", "^1.0.0"), ("gamma", "^1.0.0"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["alpha", "gamma", "beta"], result.Model!.Modules.Select(pin => pin.Id));
        Assert.Equal(
            ["modules/beta/Item.json", "modules/beta/module.json"],
            result.Resources.Select(resource => resource.Path).Where(path => path.StartsWith("modules/beta/", StringComparison.Ordinal)));
        Assert.Single(result.Model.Entities, entity => entity.Name == "beta/Item");
    }

    [Fact]
    public void An_application_field_naming_a_module_it_only_reaches_through_another_is_undeclared()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "^1.0.0")));
        Install(modules, "beta", "1.0.0", BetaEntityId);
        using var folder = Application(("alpha", "^1.0.0")).With("Request.json", """
            { "id": "a1b2c3d4-0000-4000-8000-000000000001", "kind": "entity", "name": "Request", "formatVersion": 1,
              "fields": [ { "name": "item", "type": "reference", "target": "beta/Item" } ] }
            """);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            ("AXC0207", "Request.json", "/fields/0/target"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void An_application_data_source_over_an_entity_of_a_dependency_only_module_is_undeclared()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "^1.0.0")));
        Install(modules, "beta", "1.0.0", BetaEntityId);
        using var folder = Application(("alpha", "^1.0.0")).With("data-sources/items.json", """
            { "id": "99999999-9999-4999-8999-999999999902", "kind": "dataSource", "name": "Items", "formatVersion": 1,
              "entity": "beta/Item", "fields": [ { "name": "name", "path": "name" } ] }
            """);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            ("AXC0207", "data-sources/items.json", "/entity"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_dependency_only_module_that_names_a_missing_entity_of_its_own_keeps_the_unknown_target_code()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "^1.0.0")));
        Install(modules, "beta", "1.0.0", BetaEntityId);
        modules.With("beta/1.0.0/Item.json", $$"""
            { "id": "{{BetaEntityId}}", "kind": "entity", "name": "Item", "formatVersion": 1,
              "fields": [ { "name": "other", "type": "reference", "target": "Missing" } ] }
            """);
        using var folder = Application(("alpha", "^1.0.0"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownReferenceTarget, "modules/beta/Item.json", "/fields/0/target"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void An_unknown_module_in_a_module_manifest_is_reported_in_that_manifest()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "^1.0.0")));
        using var folder = Application(("alpha", "^1.0.0"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownModule, "modules/alpha/module.json", "/modules/0/id"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_dependency_without_a_text_for_an_application_locale_is_reported_at_the_modules_list()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "^1.0.0")));
        Install(modules, "beta", "1.0.0", BetaEntityId);
        modules.With("alpha/1.0.0/TextsEn.json", Texts("c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b01", """{ "alpha.title": "Alpha" }"""));
        using var folder = Application(("alpha", "^1.0.0"))
            .With("TextsEn.json", Texts("c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b02", """{ "sample.title": "Sample" }"""));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingModuleLocale, "application.json", "/modules"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'beta'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_application_text_key_with_the_prefix_of_a_dependency_is_reported()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "^1.0.0")));
        Install(modules, "beta", "1.0.0", BetaEntityId);
        modules.With("alpha/1.0.0/TextsEn.json", Texts("c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b01", """{ "alpha.title": "Alpha" }"""));
        modules.With("beta/1.0.0/TextsEn.json", Texts("c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b03", """{ "beta.title": "Beta" }"""));
        using var folder = Application(("alpha", "^1.0.0"))
            .With("TextsEn.json", Texts("c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b02", """{ "beta.title": "Mine" }"""));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ApplicationTextKeyWithModulePrefix, "TextsEn.json", "/texts/beta.title"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Rebuilding_a_release_without_installed_modules_gives_the_same_graph()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "alpha", "1.0.0", AlphaEntityId, Depends(("beta", "^1.0.0")));
        Install(modules, "beta", "1.0.0", BetaEntityId);
        Install(modules, "beta", "1.0.4", BetaEntityId);
        using var folder = Application(("alpha", "^1.0.0"));
        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(rebuilt.Diagnostics);
        Assert.Equal(compiled.Model!.Modules, rebuilt.Model!.Modules);
        Assert.Equal("1.0.4", rebuilt.Model.Modules[1].Version);
        Assert.Equal(compiled.Resources.Select(resource => (resource.Path, resource.Content)), rebuilt.Resources.Select(resource => (resource.Path, resource.Content)));
        Assert.Equal(compiled.ContentHash, rebuilt.ContentHash);
    }

    private static void Install(TemporaryFolder root, string id, string version, string entityId, string modules = "")
    {
        root.With($"{id}/{version}/module.json", $$"""
            { "kind": "module", "id": "{{id}}", "version": "{{version}}", "label": "{{id}}"{{modules}} }
            """);
        root.With($"{id}/{version}/Item.json", $$"""
            { "id": "{{entityId}}", "kind": "entity", "name": "Item", "formatVersion": 1,
              "fields": [ { "name": "name", "type": "text", "required": true } ] }
            """);
    }

    private static string Depends(params (string Id, string Version)[] entries) =>
        $$""", "modules": [{{string.Join(", ", entries.Select(entry => $$"""{ "id": "{{entry.Id}}", "version": "{{entry.Version}}" }"""))}}]""";

    private static TemporaryFolder Application(params (string Id, string Version)[] entries) =>
        new TemporaryFolder().With("application.json", $$"""
            { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
              "modules": [{{string.Join(", ", entries.Select(entry => $$"""{ "id": "{{entry.Id}}", "version": "{{entry.Version}}" }"""))}}] }
            """);

    private static string Texts(string id, string texts) => $$"""
        { "id": "{{id}}", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en", "texts": {{texts}} }
        """;
}
