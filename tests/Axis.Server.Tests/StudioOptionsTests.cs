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

    private StudioOptions LoadInCell() =>
        StudioOptions.Load(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:ApplicationsPath"] = "apps" })
                .Build(),
            _root,
            new CellOptions { Id = "vn-1" });
}
