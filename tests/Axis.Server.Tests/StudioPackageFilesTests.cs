using Axis.Studio;

namespace Axis.Server.Tests;

public sealed class StudioPackageFilesTests
{
    [Fact]
    public void A_plain_package_and_path_name_a_file_under_packages()
    {
        Assert.Equal("packages/x/src/adapter.py", StudioPackageFiles.File("x", "src/adapter.py"));
    }

    [Theory]
    [InlineData("x", ".hidden")]
    [InlineData("x", "src/.hidden/adapter.py")]
    [InlineData("x", "../adapter.py")]
    [InlineData("x", "src/../adapter.py")]
    [InlineData("x", "src/./adapter.py")]
    [InlineData("x", "src\\adapter.py")]
    [InlineData("x", "")]
    [InlineData("x", "src//adapter.py")]
    [InlineData("x", "src/")]
    [InlineData("x", "/adapter.py")]
    [InlineData("", "adapter.py")]
    [InlineData(".", "adapter.py")]
    [InlineData("..", "adapter.py")]
    [InlineData(".x", "adapter.py")]
    [InlineData("x/y", "adapter.py")]
    [InlineData("x\\y", "adapter.py")]
    public void A_hidden_segment_a_dot_segment_a_backslash_an_empty_path_and_a_package_that_is_not_plain_name_no_file(
        string package, string path)
    {
        Assert.Null(StudioPackageFiles.File(package, path));
    }
}
