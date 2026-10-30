using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the <c>packages</c> of an application manifest or of a module manifest against the
/// installed package versions and pins each one. A package id appears at most once, matched exactly.
/// A version range is an exact version, <c>^X.Y.Z</c> or <c>~X.Y.Z</c>, see <see cref="VersionRange"/>. A package with no
/// installed version is unknown, and a known package with no installed version in its range cannot
/// be pinned. Otherwise the highest installed version in the range is pinned. A release pins one
/// version of each package id, shared by the application and its modules, see
/// <see cref="CheckModules"/>.
/// </summary>
internal static class PackageChecker
{
    /// <summary>
    /// Checks <paramref name="entries"/>, the packages written in <paramref name="file"/>, and returns the pins of the entries without problems, in the order written.
    /// Each reference is added for <paramref name="owner"/>, which is null for a module manifest, because it is not a resource.
    /// </summary>
    public static IReadOnlyList<PinnedPackageModel> Check(
        IReadOnlyList<ApplicationPackageDefinition> entries,
        IReadOnlyCollection<PackageVersion> installed,
        string file,
        Guid? resourceId,
        Resource? owner,
        List<Diagnostic> diagnostics,
        ReferenceIndexBuilder references) =>
        [.. CheckEntries(entries, installed, file, resourceId, owner, diagnostics, references).Select(entry => entry.Pin)];

    private static List<(int Index, PinnedPackageModel Pin)> CheckEntries(
        IReadOnlyList<ApplicationPackageDefinition> entries,
        IReadOnlyCollection<PackageVersion> installed,
        string file,
        Guid? resourceId,
        Resource? owner,
        List<Diagnostic> diagnostics,
        ReferenceIndexBuilder references)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, file, path, resourceId));

        var pins = new List<(int Index, PinnedPackageModel Pin)>();
        var firstIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            var package = entries[index];
            var path = $"/packages/{index}";
            if (!firstIndexById.TryAdd(package.Id, index))
            {
                Report(
                    DiagnosticCodes.DuplicateApplicationPackage,
                    $"The package '{package.Id}' is already listed at '/packages/{firstIndexById[package.Id]}'. List each package once.",
                    $"{path}/id");
                continue;
            }

            var versions = VersionsOf(installed, package.Id);
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

            pins.Add((index, new PinnedPackageModel(package.Id, package.Version, pinned)));
            if (owner is not null)
            {
                references.Add(owner, path, ReferenceRelations.Package, new PackageNode(package.Id));
            }
        }

        return pins;
    }

    private static List<string> VersionsOf(IReadOnlyCollection<PackageVersion> installed, string id) =>
        installed
            .Where(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal))
            .Select(candidate => candidate.Version)
            .Distinct(StringComparer.Ordinal)
            .Order(Comparer<string>.Create(PackageVersions.Compare))
            .ToList();

    /// <summary>
    /// Checks the <c>packages</c> of each pinned module and pins them with the release's pins. A release
    /// pins at most one version of a package id. A module that lists a package the application lists
    /// too uses the application's pin, and its entry is reported when that pin is not in the entry's
    /// range. Modules that list a package the application does not share the highest installed
    /// version that meets all their ranges, and each of their entries is reported when there is none.
    /// Returns the pins of each module by module id, and the pins the application lacks, whose range is
    /// the first module's, in module order.
    /// </summary>
    public static (IReadOnlyDictionary<string, IReadOnlyList<PinnedPackageModel>> ByModule, IReadOnlyList<PinnedPackageModel> Added) CheckModules(
        ApplicationManifest application,
        IReadOnlyList<PinnedPackageModel> applicationPins,
        IReadOnlyList<(string ModuleId, ModuleManifest Manifest)> modules,
        IReadOnlyCollection<PackageVersion> installed,
        List<Diagnostic> diagnostics,
        ReferenceIndexBuilder references)
    {
        var applicationIds = new HashSet<string>(application.Packages.Select(package => package.Id), StringComparer.Ordinal);
        var applicationPinById = applicationPins.ToDictionary(pin => pin.Id, StringComparer.Ordinal);
        var checkedModules = new List<(string ModuleId, string File, ModuleManifest Manifest, List<(int Index, PinnedPackageModel Pin)> Entries)>();
        foreach (var (moduleId, manifest) in modules)
        {
            var file = $"{ApplicationLoader.ModulesFolderPrefix}{moduleId}/{ModuleCatalog.ManifestFileName}";
            checkedModules.Add((moduleId, file, manifest, CheckEntries(manifest.Packages, installed, file, null, null, diagnostics, references)));
        }

        // Entries the checks accepted by themselves, grouped by package id, that the application does not list.
        var askersById = new Dictionary<string, List<(string File, int Index, PinnedPackageModel Entry)>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var (_, file, _, entries) in checkedModules)
        {
            foreach (var (index, pin) in entries.Where(entry => !applicationIds.Contains(entry.Pin.Id)))
            {
                if (!askersById.TryGetValue(pin.Id, out var askers))
                {
                    askersById[pin.Id] = askers = [];
                    order.Add(pin.Id);
                }

                askers.Add((file, index, pin));
            }
        }

        var added = new List<PinnedPackageModel>();
        var chosenById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in order)
        {
            var askers = askersById[id];
            var versions = VersionsOf(installed, id);
            var ranges = askers.Select(asker => VersionRange.TryParse(asker.Entry.Range, out var range) ? range : null).ToList();
            var chosen = versions.LastOrDefault(version => ranges.All(range => range!.Matches(version)));
            if (chosen is null)
            {
                foreach (var (file, index, entry) in askers)
                {
                    var others = askers.Where(asker => asker.Entry.Range != entry.Range || asker.File != file).Select(asker => $"'{asker.Entry.Range}'").Distinct().ToList();
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.NoPackageVersionInRange,
                        $"No installed version of package '{id}' is in the range '{entry.Range}' and in the ranges of the other modules ({string.Join(", ", others)}). The installed versions are {string.Join(", ", versions.Select(version => $"'{version}'"))}.",
                        file,
                        $"/packages/{index}/version"));
                }

                continue;
            }

            chosenById[id] = chosen;
            added.Add(new PinnedPackageModel(id, askers[0].Entry.Range, chosen));
        }

        var byModule = new Dictionary<string, IReadOnlyList<PinnedPackageModel>>(StringComparer.Ordinal);
        foreach (var (moduleId, file, _, entries) in checkedModules)
        {
            var pins = new List<PinnedPackageModel>();
            foreach (var (index, entry) in entries)
            {
                if (applicationIds.Contains(entry.Id))
                {
                    // The application's entry failed, which is reported already.
                    if (applicationPinById.GetValueOrDefault(entry.Id) is not { } applicationPin)
                    {
                        continue;
                    }

                    if (!VersionRange.TryParse(entry.Range, out var range) || !range.Matches(applicationPin.Version))
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.NoPackageVersionInRange,
                            $"The application pins package '{entry.Id}' at '{applicationPin.Version}', which is not in the range '{entry.Range}'.",
                            file,
                            $"/packages/{index}/version"));
                        continue;
                    }

                    pins.Add(entry with { Version = applicationPin.Version });
                }
                else if (chosenById.TryGetValue(entry.Id, out var chosen))
                {
                    pins.Add(entry with { Version = chosen });
                }
            }

            byModule[moduleId] = pins;
        }

        return (byModule, added);
    }
}
