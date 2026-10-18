using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the <c>packages</c> of an application manifest against the installed package versions
/// and pins each one. A package id appears at most once, matched exactly. A version range is an
/// exact version, <c>^X.Y.Z</c> or <c>~X.Y.Z</c>, see <see cref="VersionRange"/>. A package with no
/// installed version is unknown, and a known package with no installed version in its range cannot
/// be pinned. Otherwise the highest installed version in the range is pinned.
/// </summary>
internal static class PackageChecker
{
    /// <summary>Checks <paramref name="application"/>'s packages and returns the pins of the entries without problems, in the order written.</summary>
    public static IReadOnlyList<PinnedPackageModel> Check(
        ApplicationManifest application,
        IReadOnlyCollection<PackageVersion> installed,
        List<Diagnostic> diagnostics,
        ReferenceIndexBuilder references)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, application.File, path, application.Id));

        var pins = new List<PinnedPackageModel>();
        var firstIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < application.Packages.Count; index++)
        {
            var package = application.Packages[index];
            var path = $"/packages/{index}";
            if (!firstIndexById.TryAdd(package.Id, index))
            {
                Report(
                    DiagnosticCodes.DuplicateApplicationPackage,
                    $"The package '{package.Id}' is already listed at '/packages/{firstIndexById[package.Id]}'. List each package once.",
                    $"{path}/id");
                continue;
            }

            var versions = installed
                .Where(candidate => string.Equals(candidate.Id, package.Id, StringComparison.Ordinal))
                .Select(candidate => candidate.Version)
                .Distinct(StringComparer.Ordinal)
                .Order(Comparer<string>.Create(PackageVersions.Compare))
                .ToList();
            if (!VersionRange.TryParse(package.Version, out var range))
            {
                Report(
                    DiagnosticCodes.InvalidPackageRange,
                    $"The version range '{package.Version}' is not valid. Use an exact version such as '1.2.0', '^1.2.0' for any later version with the same major version, or '~1.2.0' for any later version with the same minor version.",
                    $"{path}/version");
            }

            if (versions.Count == 0)
            {
                Report(
                    DiagnosticCodes.UnknownPackage,
                    $"The package '{package.Id}' is not installed. No version of it is installed.",
                    $"{path}/id");
                continue;
            }

            if (range is null)
            {
                continue;
            }

            var pinned = versions.LastOrDefault(range.Matches);
            if (pinned is null)
            {
                Report(
                    DiagnosticCodes.NoPackageVersionInRange,
                    $"No installed version of package '{package.Id}' is in the range '{package.Version}'. The installed versions are {string.Join(", ", versions.Select(version => $"'{version}'"))}.",
                    $"{path}/version");
                continue;
            }

            pins.Add(new PinnedPackageModel(package.Id, package.Version, pinned));
            references.Add(application, path, ReferenceRelations.Package, new PackageNode(package.Id));
        }

        return pins;
    }
}
