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
