using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;

namespace Axis.Configuration.Tests;

public sealed class ReferenceIndexTests
{
    private const string ApplicationWithPackageAndModule = """
        { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1,
          "packages": [ { "id": "test-package", "version": "1.0.0" } ],
          "modules": [ { "id": "departments", "version": "1.0.0" } ] }
        """;

    private const string TextsEn = """
        { "id": "22222222-2222-4222-8222-222222222221", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "order.label": "Order" } }
        """;

    private const string TextsVi = """
        { "id": "22222222-2222-4222-8222-222222222222", "kind": "text", "name": "TextsVi", "formatVersion": 1, "locale": "vi",
          "texts": { "order.label": "Order (vi)" } }
        """;

    [Fact]
    public void A_text_key_used_by_an_entity_label_and_a_field_label_lists_both_and_each_locale_entry()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", Order("order.label", "order.label"))
            .With("texts/en.json", TextsEn)
            .With("texts/vi.json", TextsVi);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [
                ("TextsEn", "/texts/order.label", ReferenceRelations.TextEntry),
                ("TextsVi", "/texts/order.label", ReferenceRelations.TextEntry),
                ("Order", "/label/textKey", ReferenceRelations.LabelText),
                ("Order", "/fields/0/label/textKey", ReferenceRelations.LabelText),
            ],
            result.References.UsedBy(new TextKeyNode("order.label")).Select(edge => (edge.From.Name, edge.Path, edge.Relation)));
    }

    [Fact]
    public void An_application_with_a_pinned_package_and_module_uses_both()
    {
        using var modules = new TemporaryFolder().With("departments/1.0.0/module.json", ModuleCatalogTests.Manifest("departments", "1.0.0"));
        using var folder = new TemporaryFolder().With("application.json", ApplicationWithPackageAndModule);

        var result = ApplicationCompiler.Compile(folder.Path, [new PackageVersion("test-package", "1.0.0")], ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var application = ResourceNode.Of(result.Model!.Manifest);
        Assert.Equal(
            [
                new ReferenceEdge(application, "/packages/0", ReferenceRelations.Package, new PackageNode("test-package")),
                new ReferenceEdge(application, "/modules/0", ReferenceRelations.Module, new ModuleNode("departments")),
            ],
            result.References.Uses(application));
    }

    [Fact]
    public void A_missing_label_key_is_reported_and_adds_no_edge_while_an_existing_key_keeps_its_edge()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", Order("order.label", "order.missing"))
            .With("texts/en.json", TextsEn);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTextKey, "entities/order.json", "/fields/0/label/textKey"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        var labelEdge = Assert.Single(result.References.Edges, edge => edge.Relation == ReferenceRelations.LabelText);
        Assert.Equal(("Order", "/label/textKey"), (labelEdge.From.Name, labelEdge.Path));
        Assert.Equal<ReferenceNode>(new TextKeyNode("order.label"), labelEdge.To);
        Assert.Empty(result.References.UsedBy(new TextKeyNode("order.missing")));
    }

    [Fact]
    public void A_folder_with_errors_still_returns_its_reference_index()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", Order("order.label", "order.missing"))
            .With("texts/en.json", TextsEn);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.True(result.HasErrors);
        Assert.Null(result.Model);
        Assert.Contains(result.References.Edges, edge => edge.Relation == ReferenceRelations.LabelText && edge.From.Name == "Order");
    }

    private static string Order(string entityLabelKey, string fieldLabelKey) => $$"""
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1,
          "label": { "textKey": "{{entityLabelKey}}" },
          "fields": [ { "name": "number", "type": "text", "required": true, "label": { "textKey": "{{fieldLabelKey}}" } } ] }
        """;
}
