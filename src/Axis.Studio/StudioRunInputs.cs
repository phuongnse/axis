using Axis.Configuration.Loading;

namespace Axis.Studio;

/// <summary>
/// The saved run inputs of a package, kept in <c>run-inputs/&lt;package&gt;.json</c> at the root of the working folder, see
/// <c>docs/reference/studio-packages.md#the-authoring-api</c>. The loader skips that folder, so no compile, release or build holds
/// the file, and it is a <c>*.json</c> file, so a revision does.
/// </summary>
internal static class StudioRunInputs
{
    /// <summary>The path relative to the folder of the run inputs of <paramref name="package"/>, or null when it is not a plain folder name.</summary>
    public static string? File(string package) =>
        StudioPackageFiles.IsPackage(package) ? $"{ApplicationLoader.RunInputsFolderPrefix}{package}.json" : null;
}
