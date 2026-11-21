using Axis.Configuration.Releases;
using Axis.Server.Studio;
using Axis.Studio;

namespace Axis.Server.Tests;

/// <summary>
/// The release compare of Studio reads resources as JSON. An application's package files under <c>packages/</c>, see
/// docs/reference/package-runners.md#an-applications-packages, are not resources, so a release that holds a package
/// compares and lists without an error and the files are not shown as changes. A package's saved run inputs under
/// <c>run-inputs/</c> are not resources either.
/// </summary>
public sealed class StudioReleaseChangesTests
{
    // A release stores canonical JSON.
    private const string Entity = """{"id":"11111111-1111-4111-8111-111111111111","kind":"entity","name":"Order"}""";

    private static readonly ResourceContent[] _package =
    [
        new("packages/tools/axis-package.json", """{ "id": "tools" }"""),
        new("packages/tools/adapter.py", "import sys\n"),
    ];

    [Fact]
    public void A_release_that_holds_a_package_compares_without_reading_its_files_as_resources()
    {
        ResourceContent[] baseline = [new("entities/order.json", Entity), .. _package];
        ResourceContent[] current = [new("entities/order.json", Entity), .. _package, new("entities/line.json", Entity)];

        var changes = StudioReleaseChanges.Compare(current, baseline);

        var change = Assert.Single(changes);
        Assert.Equal(("entities/line.json", "added"), (change.Path, change.Change));
    }

    [Fact]
    public void A_folder_without_the_package_of_the_release_does_not_show_its_files_as_removed()
    {
        ResourceContent[] release = [new("entities/order.json", Entity), .. _package];
        StudioSnapshotFile[] folder = [new("entities/order.json", "v1", Entity)];

        Assert.Empty(StudioReleaseChanges.Compare(folder, release));
    }

    [Fact]
    public void A_folder_file_under_run_inputs_is_no_change()
    {
        ResourceContent[] release = [new("entities/order.json", Entity)];
        StudioSnapshotFile[] folder = [new("entities/order.json", "v1", Entity), new("run-inputs/x.json", "v2", """{"items":[]}""")];

        Assert.Empty(StudioReleaseChanges.Compare(folder, release));
    }
}
