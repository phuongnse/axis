using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;

namespace Axis.Configuration.Tests;

public sealed class ReleaseCompilerBuildModelTests
{
    private static readonly Guid _applicationId = Guid.Parse("0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01");

    private const string Manifest = """
        { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }
        """;

    private const string ManifestWithPackages = """
        { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1,
          "packages": [{ "id": "test-package", "version": "^1.0.0" }] }
        """;

    private const string Order = """
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1,
          "fields": [ { "name": "number", "type": "text", "maxLength": 20 } ] }
        """;

    [Fact]
    public void Stored_release_compiles_back_into_its_model()
    {
        var compiled = Compile();
        var release = StoredRelease(compiled);

        var model = ReleaseCompiler.BuildModel(release);

        Assert.NotNull(compiled.Model);
        ModelAssert.Equal(compiled.Model, model);
        Assert.Equal(["number"], Assert.Single(model.Entities).Fields.Select(field => field.Name));
    }

    [Fact]
    public void Stored_release_keeps_the_text_files_and_compiles_back_into_its_texts()
    {
        var compiled = ApplicationCompiler.Compile(
        [
            new ResourceContent("application.json", Manifest),
            new ResourceContent("entities/order.json", Order),
            new ResourceContent("texts/en.json", """{ "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en", "texts": { "order.label": "Order" } }"""),
            new ResourceContent("texts/vi.json", """{ "id": "44444444-4444-4444-8444-444444444444", "kind": "text", "name": "TextsVi", "formatVersion": 1, "locale": "vi", "texts": { "order.label": "Đơn hàng" } }"""),
        ]);
        Assert.Empty(compiled.Diagnostics);
        var release = StoredRelease(compiled);

        var model = ReleaseCompiler.BuildModel(release);

        Assert.Equal(["application.json", "entities/order.json", "texts/en.json", "texts/vi.json"], release.Resources.Select(resource => resource.Path));
        Assert.NotNull(compiled.Model);
        ModelAssert.Equal(compiled.Model, model);
        Assert.Equal(["en", "vi"], model.Texts.Select(text => text.Locale));
        Assert.Equal("Đơn hàng", model.Texts[1].Texts["order.label"]);
    }

    [Fact]
    public void Stored_release_of_the_valid_fixture_compiles_back_into_its_sites_and_pages()
    {
        var compiled = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid-app"));
        Assert.Empty(compiled.Diagnostics);
        var release = StoredRelease(compiled);

        var model = ReleaseCompiler.BuildModel(release);

        Assert.NotNull(compiled.Model);
        ModelAssert.Equal(compiled.Model, model);
        Assert.Equal("purchasing", Assert.Single(model.Sites).Path);
        Assert.Equal(["PurchaseRequestForm", "PurchaseRequests"], model.Pages.Select(page => page.Name));
    }

    [Fact]
    public void Stored_release_keeps_the_data_source_files_and_compiles_back_into_its_data_sources()
    {
        var compiled = ApplicationCompiler.Compile(
        [
            new ResourceContent("application.json", Manifest),
            new ResourceContent("entities/order.json", Order),
            new ResourceContent("data-sources/orders.json", """{ "id": "99999999-9999-4999-8999-999999999991", "kind": "dataSource", "name": "Orders", "formatVersion": 1, "entity": "Order", "fields": [{ "name": "orderNumber", "path": "number" }], "sort": "-orderNumber", "pageSize": 5 }"""),
        ]);
        Assert.Empty(compiled.Diagnostics);
        var release = StoredRelease(compiled);

        var model = ReleaseCompiler.BuildModel(release);

        Assert.Equal(["application.json", "data-sources/orders.json", "entities/order.json"], release.Resources.Select(resource => resource.Path));
        Assert.NotNull(compiled.Model);
        ModelAssert.Equal(compiled.Model, model);
        var dataSource = Assert.Single(model.DataSources);
        Assert.Equal(("orderNumber", "number"), (dataSource.Fields[0].Name, dataSource.Fields[0].Field.Name));
        Assert.Equal(5, dataSource.PageSize);
    }

    [Fact]
    public void Stored_release_keeps_the_process_files_and_compiles_back_into_its_processes()
    {
        var compiled = ApplicationCompiler.Compile(
        [
            new ResourceContent("application.json", Manifest),
            new ResourceContent("entities/order.json", Order),
            new ResourceContent("texts/en.json", """{ "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en", "texts": { "order.cannotStart": "Cannot start" } }"""),
            new ResourceContent("processes/review.json", """
                { "id": "77777777-7777-4777-8777-777777777701", "kind": "process", "name": "Review", "formatVersion": 1, "entity": "Order",
                  "startCondition": { "expression": "number is not null", "message": { "textKey": "order.cannotStart" } },
                  "start": "check",
                  "steps": [
                    { "name": "check", "type": "decision", "branches": [{ "when": "length(number) > 5", "next": "long" }], "otherwise": "short" },
                    { "name": "long", "type": "end" },
                    { "name": "short", "type": "end" }
                  ] }
                """),
        ]);
        Assert.Empty(compiled.Diagnostics);
        var release = StoredRelease(compiled);

        var model = ReleaseCompiler.BuildModel(release);

        Assert.Contains("processes/review.json", release.Resources.Select(resource => resource.Path));
        Assert.NotNull(compiled.Model);
        ModelAssert.Equal(compiled.Model, model);
        Assert.True(model.TryGetProcess("review", out var process));
        Assert.Equal(["check", "long", "short"], process.Steps.Select(step => step.Name));
    }

    [Fact]
    public void Stored_release_compiles_an_operation_step_back_with_its_fields_in_declaration_order()
    {
        // The stored file sorts the set keys, so the model lists them in declaration order instead.
        var compiled = ApplicationCompiler.Compile(
        [
            new ResourceContent("application.json", Manifest),
            new ResourceContent("entities/ticket.json", """
                { "id": "11111111-1111-4111-8111-111111111112", "kind": "entity", "name": "Ticket", "formatVersion": 1,
                  "fields": [ { "name": "title", "type": "text" }, { "name": "code", "type": "text" } ] }
                """),
            new ResourceContent("processes/close.json", """
                { "id": "77777777-7777-4777-8777-777777777702", "kind": "process", "name": "Close", "formatVersion": 1, "entity": "Ticket",
                  "start": "close",
                  "steps": [
                    { "name": "close", "type": "operation", "operation": "updateRecord", "set": { "title": "'Closed'", "code": "'C'" }, "next": "done" },
                    { "name": "done", "type": "end" }
                  ] }
                """),
        ]);
        Assert.Empty(compiled.Diagnostics);
        var release = StoredRelease(compiled);

        var model = ReleaseCompiler.BuildModel(release);

        Assert.NotNull(compiled.Model);
        ModelAssert.Equal(compiled.Model, model);
        Assert.True(model.TryGetProcess("close", out var process));
        var close = Assert.IsType<OperationStepModel>(process.Steps[0]);
        Assert.Equal(["title", "code"], close.Set.Select(assignment => assignment.Field));
    }

    [Fact]
    public void Stored_release_with_pins_compiles_back_against_its_own_pins_only()
    {
        var compiled = ApplicationCompiler.Compile(
            [new ResourceContent("application.json", ManifestWithPackages), new ResourceContent("entities/order.json", Order)],
            [new PackageVersion("test-package", "1.0.0"), new PackageVersion("test-package", "1.2.0")]);
        Assert.Empty(compiled.Diagnostics);
        var release = StoredRelease(compiled);

        var model = ReleaseCompiler.BuildModel(release);

        Assert.Equal([("test-package", "1.2.0")], release.Packages.Select(package => (package.PackageId, package.Version)));
        Assert.NotNull(compiled.Model);
        ModelAssert.Equal(compiled.Model, model);
        Assert.Equal([new PinnedPackageModel("test-package", "^1.0.0", "1.2.0")], model.Packages);
    }

    [Fact]
    public void Stored_release_with_a_package_step_compiles_back_against_the_installed_package()
    {
        using var packages = new TemporaryFolder().Install(1);
        var installed = PackageCatalog.Load(packages.Path).Versions;
        var compiled = ApplicationCompiler.Compile(
            [
                new ResourceContent("application.json", ManifestWithPackages),
                new ResourceContent("entities/order.json", """
                    { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1,
                      "fields": [ { "name": "total", "type": "integer" } ] }
                    """),
                new ResourceContent("processes/add.json", """
                    { "id": "77777777-7777-4777-8777-777777777701", "kind": "process", "name": "Add", "formatVersion": 1,
                      "entity": "Order", "start": "add",
                      "steps": [
                        { "name": "add", "type": "operation", "operation": "test-package/sum",
                          "input": { "a": "1", "b": "total" }, "output": { "total": "total" }, "next": "done" },
                        { "name": "done", "type": "end" } ] }
                    """),
            ],
            installed);
        Assert.Empty(compiled.Diagnostics);
        var release = StoredRelease(compiled);

        var model = ReleaseCompiler.BuildModel(release, installed);

        Assert.NotNull(compiled.Model);
        ModelAssert.Equal(compiled.Model, model);
        var step = Assert.IsType<PackageOperationStepModel>(Assert.Single(model.Processes).Steps[0]);
        Assert.Equal(("test-package", "1.0.0", "sum", TimeSpan.FromSeconds(5)), (step.Package, step.Version, step.Operation, step.Timeout));
        Assert.Throws<InvalidOperationException>(() => ReleaseCompiler.BuildModel(release));
    }

    [Fact]
    public void Stored_release_whose_pin_is_missing_or_out_of_range_throws()
    {
        var compiled = ApplicationCompiler.Compile(
            [new ResourceContent("application.json", ManifestWithPackages), new ResourceContent("entities/order.json", Order)],
            [new PackageVersion("test-package", "1.2.0")]);
        var missing = StoredRelease(compiled);
        missing.Packages.Clear();
        var outOfRange = StoredRelease(compiled);
        outOfRange.Packages[0] = new ReleasePackage { ReleaseId = outOfRange.Id, PackageId = "test-package", Version = "2.0.0" };

        Assert.Throws<InvalidOperationException>(() => ReleaseCompiler.BuildModel(missing));
        Assert.Throws<InvalidOperationException>(() => ReleaseCompiler.BuildModel(outOfRange));
    }

    [Fact]
    public void Stored_release_with_another_pin_in_range_throws()
    {
        var compiled = ApplicationCompiler.Compile(
            [new ResourceContent("application.json", ManifestWithPackages), new ResourceContent("entities/order.json", Order)],
            [new PackageVersion("test-package", "1.2.0")]);
        var release = StoredRelease(compiled);
        release.Packages[0] = new ReleasePackage { ReleaseId = release.Id, PackageId = "test-package", Version = "1.3.0" };

        Assert.Throws<InvalidOperationException>(() => ReleaseCompiler.BuildModel(release));
    }

    [Fact]
    public void Stored_release_with_invalid_content_throws()
    {
        var release = StoredRelease(Compile());
        release.Resources[1] = new ReleaseResource { ReleaseId = release.Id, Path = release.Resources[1].Path, Content = "{" };

        Assert.Throws<InvalidOperationException>(() => ReleaseCompiler.BuildModel(release));
    }

    [Fact]
    public void Stored_release_with_another_content_hash_or_application_id_throws()
    {
        var compiled = Compile();

        Assert.Throws<InvalidOperationException>(() => ReleaseCompiler.BuildModel(StoredRelease(compiled, contentHash: new string('0', 64))));
        Assert.Throws<InvalidOperationException>(() => ReleaseCompiler.BuildModel(StoredRelease(compiled, applicationId: Guid.NewGuid())));
    }

    private static CompilationResult Compile()
    {
        var compiled = ApplicationCompiler.Compile([new ResourceContent("application.json", Manifest), new ResourceContent("entities/order.json", Order)]);
        Assert.Empty(compiled.Diagnostics);
        return compiled;
    }

    /// <summary>The release as stored: canonical content, its content hash and its application id, unless overridden.</summary>
    private static Release StoredRelease(CompilationResult compiled, string? contentHash = null, Guid? applicationId = null)
    {
        var id = Guid.NewGuid();
        return new Release
        {
            Id = id,
            ApplicationId = applicationId ?? _applicationId,
            ContentHash = contentHash ?? compiled.ContentHash!,
            CreatedAt = DateTimeOffset.UtcNow,
            Resources = [.. compiled.Resources.Select(resource => new ReleaseResource { ReleaseId = id, Path = resource.Path, Content = resource.Content })],
            Packages = [.. compiled.Model!.Packages.Select(package => new ReleasePackage { ReleaseId = id, PackageId = package.Id, Version = package.Version })],
        };
    }
}
