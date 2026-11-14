using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the packages an application holds under <c>packages/&lt;id&gt;/</c>, see
/// <c>docs/reference/extension-packages.md#an-applications-packages</c>. Each package's
/// <c>axis-package.json</c> is read with the schema and rules of an installed package's manifest, its id must be its
/// folder name and no installed package may use it, and no package is allowed while <c>allowed</c> is false, except a
/// customer-hosted one, which holds only its manifest. Every problem is reported at the package's manifest file, except an
/// extra file of a customer-hosted package, which is reported at that file.
/// </summary>
internal static class ApplicationPackageChecker
{
    /// <summary>
    /// Checks <paramref name="packageFiles"/>, the files under <c>packages/</c>, and returns the operations of each package
    /// without problems, by id. <paramref name="seenIds"/> holds the folder name of every package, including a broken one, so
    /// a step that calls a broken package is not reported again.
    /// </summary>
    public static IReadOnlyDictionary<string, PinnedOperations> Check(
        IReadOnlyList<ResourceContent> packageFiles,
        IReadOnlyCollection<PackageVersion> installed,
        bool allowed,
        List<Diagnostic> diagnostics,
        out IReadOnlySet<string> seenIds)
    {
        var found = new Dictionary<string, PinnedOperations>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        seenIds = ids;

        var folders = new SortedDictionary<string, (ResourceContent? Manifest, List<string> Others)>(StringComparer.Ordinal);
        foreach (var file in packageFiles)
        {
            var segments = file.Path[ApplicationLoader.PackagesFolderPrefix.Length..].Split('/');
            if (segments.Length == 1)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.PackageManifestInvalid,
                    $"The file '{segments[0]}' is not in a package folder. A package lives in 'packages/<id>/'.",
                    file.Path,
                    ""));
                continue;
            }

            var (manifest, others) = folders.GetValueOrDefault(segments[0], (null, []));
            if (segments.Length == 2 && segments[1] == PackageCatalog.ManifestFileName)
            {
                manifest = file;
            }
            else
            {
                others.Add(file.Path);
            }

            folders[segments[0]] = (manifest, others);
        }

        foreach (var (id, (manifestFile, others)) in folders)
        {
            ids.Add(id);
            var file = $"{ApplicationLoader.PackagesFolderPrefix}{id}/{PackageCatalog.ManifestFileName}";
            var label = $"Package '{id}'";
            var problems = new List<Diagnostic>();
            PackageManifest? manifest = null;
            if (manifestFile is null)
            {
                problems.Add(new Diagnostic(
                    DiagnosticCodes.PackageManifestInvalid,
                    $"{label}: The manifest '{PackageCatalog.ManifestFileName}' is missing.",
                    file,
                    ""));
            }
            else
            {
                manifest = PackageCatalog.ReadManifestContent(manifestFile.Content, file, label, problems);
            }

            // A customer-hosted package needs no setting, because Axis builds and runs none of it.
            var customerHosted = manifest is { CustomerHosted: true };
            if (!allowed && !customerHosted)
            {
                problems.Insert(0, new Diagnostic(
                    DiagnosticCodes.ApplicationPackagesOff,
                    "Application packages are off in this installation (Extensions:ApplicationPackages). Remove the package or ask the operator to turn them on.",
                    file,
                    ""));
            }

            if (customerHosted)
            {
                var folder = $"{ApplicationLoader.PackagesFolderPrefix}{id}/";
                foreach (var other in others)
                {
                    problems.Add(new Diagnostic(
                        DiagnosticCodes.PackageManifestInvalid,
                        $"{label}: A customer-hosted package holds only its manifest. Remove '{other[folder.Length..]}'.",
                        other,
                        ""));
                }
            }

            if (manifest is not null)
            {
                PackageCatalog.CheckManifestRules(manifest, file, label, problems);
                if (!string.Equals(manifest.Id, id, StringComparison.Ordinal))
                {
                    problems.Add(new Diagnostic(
                        DiagnosticCodes.PackageFolderMismatch,
                        $"{label}: The id '{manifest.Id}' differs from its folder name '{id}'.",
                        file,
                        "/id"));
                }

                if (installed.Any(version => string.Equals(version.Id, manifest.Id, StringComparison.Ordinal)))
                {
                    problems.Add(new Diagnostic(
                        DiagnosticCodes.ApplicationPackageIdTaken,
                        $"{label}: The package id '{manifest.Id}' is already used by a package installed for the installation. Choose another id.",
                        file,
                        "/id"));
                }

                if (problems.Count == 0)
                {
                    found[id] = new PinnedOperations(id, manifest.Version, manifest.Operations, manifest.CustomerHosted);
                }
            }

            diagnostics.AddRange(problems);
        }

        return found;
    }
}
