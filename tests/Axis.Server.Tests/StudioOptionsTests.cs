using Axis.Server.Studio;
using Axis.Tenancy;
using Microsoft.Extensions.Configuration;

namespace Axis.Server.Tests;

public sealed class StudioOptionsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("axis-studio-options-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Studio_folders_with_a_cell_id_stop_the_start_and_the_message_names_both_settings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:Folders:0"] = Path.Combine(_root, "app") })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => StudioOptions.Load(configuration, _root, new CellOptions { Id = "vn-1" }));

        Assert.Contains("Studio:Folders", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Cell:Id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Studio_folders_outside_a_cell_are_read_as_before()
    {
        var folder = Path.Combine(_root, "app");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:Folders:0"] = folder })
            .Build();

        var options = StudioOptions.Load(configuration, _root, new CellOptions());

        Assert.Equal([folder], options.Folders);
        Assert.Same(options, options.ForTenant("a"));
    }

    [Fact]
    public void In_a_cell_a_tenant_gets_its_own_folder_under_the_applications_path_created_on_first_use()
    {
        var options = LoadInCell();

        var tenant = options.ForTenant("a");

        Assert.Equal(Path.Combine(_root, "apps", "a"), tenant.ApplicationsPath);
        Assert.True(Directory.Exists(tenant.ApplicationsPath));
        Assert.Empty(tenant.Folders);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../b")]
    [InlineData("a/b")]
    public void In_a_cell_a_tenant_id_that_is_not_a_plain_folder_name_gets_no_applications_folder(string? tenantId)
    {
        var options = LoadInCell();

        var tenant = options.ForTenant(tenantId);

        Assert.Null(tenant.ApplicationsPath);
        Assert.Equal(["apps"], Directory.GetDirectories(_root).Select(Path.GetFileName));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "apps")));
    }

    [Fact]
    public void The_revisions_path_defaults_to_a_revisions_folder_under_the_applications_path()
    {
        var options = Load(new() { ["Studio:ApplicationsPath"] = "apps" });

        Assert.Equal(Path.Combine(_root, "apps", ".revisions"), options.RevisionsPath);
    }

    [Fact]
    public void A_set_revisions_path_is_resolved_against_the_content_root()
    {
        var options = Load(new() { ["Studio:ApplicationsPath"] = "apps", ["Studio:RevisionsPath"] = "history" });

        Assert.Equal(Path.Combine(_root, "history"), options.RevisionsPath);
    }

    [Fact]
    public void With_neither_path_the_server_keeps_no_revisions()
    {
        var options = Load(new() { ["Studio:Folders:0"] = Path.Combine(_root, "app") });

        Assert.Null(options.RevisionsPath);
    }

    [Fact]
    public void A_revisions_path_alone_keeps_revisions_for_the_listed_folders()
    {
        var options = Load(new() { ["Studio:Folders:0"] = Path.Combine(_root, "app"), ["Studio:RevisionsPath"] = "history" });

        Assert.Equal(Path.Combine(_root, "history"), options.RevisionsPath);
    }

    [Fact]
    public void A_revisions_path_with_a_cell_id_stops_the_start_and_the_message_names_both_settings()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => StudioOptions.Load(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:ApplicationsPath"] = "apps", ["Studio:RevisionsPath"] = "history" })
                    .Build(),
                _root,
                new CellOptions { Id = "vn-1" }));

        Assert.Contains("Studio:RevisionsPath", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Cell:Id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void In_a_cell_the_revisions_of_a_tenant_live_in_its_own_folder_and_a_request_without_a_tenant_has_none()
    {
        var options = LoadInCell();

        Assert.Equal(Path.Combine(_root, "apps", "a", ".revisions"), options.ForTenant("a").RevisionsPath);
        Assert.Null(options.ForTenant(null).RevisionsPath);
    }

    private StudioOptions Load(Dictionary<string, string?> settings) =>
        StudioOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), _root, new CellOptions());

    private StudioOptions LoadInCell() =>
        StudioOptions.Load(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:ApplicationsPath"] = "apps" })
                .Build(),
            _root,
            new CellOptions { Id = "vn-1" });
}
