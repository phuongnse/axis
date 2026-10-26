using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class ApplicationTimeZoneCompilerTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";

    [Fact]
    public void A_manifest_with_an_iana_time_zone_compiles_into_the_model_and_survives_the_stored_resources()
    {
        using var folder = Folder("""Asia/Ho_Chi_Minh""");

        var result = ApplicationCompiler.Compile(folder.Path);
        var rebuilt = ApplicationCompiler.Compile(result.Resources);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(rebuilt.Diagnostics);
        Assert.Equal("Asia/Ho_Chi_Minh", result.Model!.TimeZone.Id);
        Assert.Equal("Asia/Ho_Chi_Minh", result.Model.Manifest.TimeZone);
        Assert.Equal("Asia/Ho_Chi_Minh", rebuilt.Model!.TimeZone.Id);
    }

    [Fact]
    public void A_manifest_without_a_time_zone_is_utc()
    {
        using var folder = Folder(null);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Same(TimeZoneInfo.Utc, result.Model!.TimeZone);
        Assert.Null(result.Model.Manifest.TimeZone);
    }

    [Fact]
    public void The_utc_id_is_known()
    {
        using var folder = Folder("UTC");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(TimeSpan.Zero, result.Model!.TimeZone.BaseUtcOffset);
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    // An id is known only in its exact letter case, as for a schedule trigger.
    [InlineData("asia/ho_chi_minh")]
    [InlineData("SE Asia Standard Time")]
    public void An_unknown_time_zone_is_reported_at_its_path_and_gives_no_model(string id)
    {
        using var folder = Folder(id);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownApplicationTimeZone, "application.json", "/timeZone"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal("AXC0277", diagnostic.Code);
        Assert.Equal(Guid.Parse(ApplicationId), diagnostic.ResourceId);
        Assert.Contains($"'{id}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_empty_time_zone_is_a_schema_error()
    {
        using var folder = Folder("");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.UnknownApplicationTimeZone);
        Assert.Null(result.Model);
    }

    private static TemporaryFolder Folder(string? timeZone) =>
        new TemporaryFolder().With("application.json", $$"""
            { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1{{(timeZone is null ? "" : $$""", "timeZone": "{{timeZone}}" """)}} }
            """);
}
