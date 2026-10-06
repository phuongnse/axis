using Axis.Configuration.Compilation;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;

namespace Axis.Configuration.Tests;

public sealed class ReleaseCompilerBuildModelTests
{
    private static readonly Guid _applicationId = Guid.Parse("0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01");

    private const string Manifest = """
        { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }
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
        };
    }
}
