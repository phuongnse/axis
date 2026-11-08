using Axis.Studio;
using Axis.Tenancy;
using Microsoft.Extensions.Configuration;

namespace Axis.Server.Tests;

public sealed class StudioFolderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("axis-studio-folder-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_listed_folder_is_found_by_its_manifest_name_ignoring_letter_case()
    {
        var listed = Manifest(Path.Combine(_root, "listed"), "Purchase-Requests");
        var options = Load(new() { ["Studio:Folders:0"] = listed });

        Assert.Equal(listed, StudioFolder.Find(options, null, "purchase-requests"));
        Assert.Null(StudioFolder.Find(options, null, "other"));
    }

    [Fact]
    public void A_subfolder_of_the_applications_path_is_found_by_its_manifest_name()
    {
        var folder = Manifest(Path.Combine(_root, "apps", "created"), "created-app");
        var options = Load(new() { ["Studio:ApplicationsPath"] = "apps" });

        Assert.Equal(folder, StudioFolder.Find(options, null, "created-app"));
    }

    [Fact]
    public void In_a_cell_a_tenant_finds_only_the_applications_of_its_own_folder()
    {
        var own = Manifest(Path.Combine(_root, "apps", "a", "orders"), "orders");
        Manifest(Path.Combine(_root, "apps", "b", "billing"), "billing");
        Manifest(Path.Combine(_root, "apps", "loose"), "loose");
        var options = Load(new() { ["Studio:ApplicationsPath"] = "apps" }, new CellOptions { Id = "vn-1" });

        Assert.Equal(own, StudioFolder.Find(options, "a", "orders"));
        Assert.Null(StudioFolder.Find(options, "a", "billing"));
        Assert.Null(StudioFolder.Find(options, "a", "loose"));
        Assert.Null(StudioFolder.Find(options, "b", "orders"));
        Assert.Null(StudioFolder.Find(options, null, "orders"));
        Assert.Null(StudioFolder.Find(options, "..", "orders"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("/etc")]
    [InlineData("")]
    [InlineData(" ")]
    public void A_name_that_is_not_a_plain_name_finds_nothing(string application)
    {
        Manifest(Path.Combine(_root, "apps", "orders"), "orders");
        var options = Load(new() { ["Studio:ApplicationsPath"] = "apps" });

        Assert.Null(StudioFolder.Find(options, null, application));
    }

    [Fact]
    public void A_folder_path_finds_nothing()
    {
        var listed = Manifest(Path.Combine(_root, "listed"), "listed");
        var options = Load(new() { ["Studio:Folders:0"] = listed });

        Assert.Null(StudioFolder.Find(options, null, listed));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/b")]
    public void A_folder_whose_manifest_name_is_not_a_plain_name_is_not_found_by_it(string name)
    {
        var listed = Manifest(Path.Combine(_root, "listed"), name);
        var options = Load(new() { ["Studio:Folders:0"] = listed });

        Assert.Null(StudioFolder.Find(options, null, name));
    }

    private static string Manifest(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "application.json"), System.Text.Json.JsonSerializer.Serialize(new { name }));
        return folder;
    }

    private StudioOptions Load(Dictionary<string, string?> settings, CellOptions? cell = null) =>
        StudioOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), _root, cell ?? new CellOptions());
}
