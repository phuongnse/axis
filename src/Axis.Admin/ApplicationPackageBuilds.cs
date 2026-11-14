using System.Text;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;

namespace Axis.Admin;

/// <summary>
/// Sends each <c>packages/&lt;id&gt;/</c> of a snapshot to <c>application-package-host</c> as a <c>build</c>, and one package of it as a <c>test</c>, see
/// docs/reference/extension-packages.md#an-applications-packages. The publisher runs no pip, npm or install script itself. A <c>refused</c> answer
/// gives <c>AXC0149</c> for each problem, at the problem's file in the package. A host that gives no answer within <c>wait</c> gives
/// <c>AXC0152</c> at the package's manifest, and no message names the host's address. The files of a <c>built</c> answer are stored with a new release, see <see cref="ReleaseCompiler"/>.
/// </summary>
internal sealed class ApplicationPackageBuilds(string host, int port, TimeSpan wait)
{
    /// <summary>How long the publisher waits for the host's answer to one package.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Builds the packages of <paramref name="files"/> one at a time, in id order, and returns the diagnostics of all of them and the files of each
    /// built package, keyed by <c>packages/&lt;id&gt;/&lt;path&gt;</c>. A file directly under
    /// <c>packages/</c> is not a package's, and the compile reports it. A customer-hosted package is not sent.
    /// </summary>
    public async Task<PackageBuildResult> BuildAsync(IReadOnlyList<ResourceContent> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        var packages = PackageFiles(files)
            .Where(package => !IsCustomerHosted(package))
            .OrderBy(package => package.Key, StringComparer.Ordinal);

        var diagnostics = new List<Diagnostic>();
        var builtFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var package in packages)
        {
            var build = new BuildMessage(package.ToDictionary(file => file.Segments[1], file => Encoding.UTF8.GetBytes(file.Content), StringComparer.Ordinal));
            switch (await RunnerProtocol.AskAsync(host, port, build, wait, cancellationToken))
            {
                case BuiltMessage built:
                    foreach (var (path, content) in built.Files)
                    {
                        builtFiles[$"{ApplicationLoader.PackagesFolderPrefix}{package.Key}/{path}"] = content;
                    }

                    break;
                case RefusedMessage refused:
                    diagnostics.AddRange(Refusals(package.Key, refused));
                    break;
                default:
                    diagnostics.Add(NoAnswer(package.Key, "build", "it was neither built nor scanned"));
                    break;
            }
        }

        return new PackageBuildResult(diagnostics, builtFiles);
    }

    /// <summary>
    /// Sends the files of the package <paramref name="package"/> of <paramref name="files"/> to the host as a <c>test</c>, and returns the
    /// outcome of each test the host ran. A refused or unanswered test returns its diagnostics and no test. A package with no file
    /// is sent too, and the host refuses it at its manifest. A customer-hosted package is sent too, for the same reason.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<PackageTestResult> TestAsync(IReadOnlyList<ResourceContent> files, string package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(package);

        var own = PackageFiles(files).FirstOrDefault(group => group.Key == package)?.ToDictionary(file => file.Segments[1], file => Encoding.UTF8.GetBytes(file.Content), StringComparer.Ordinal)
            ?? new Dictionary<string, byte[]>(StringComparer.Ordinal);
        return await RunnerProtocol.AskAsync(host, port, new TestMessage(own), wait, cancellationToken) switch
        {
            TestedMessage tested => new PackageTestResult([], tested.Tests),
            RefusedMessage refused => new PackageTestResult([.. Refusals(package, refused)], []),
            _ => new PackageTestResult([NoAnswer(package, "test", "its tests did not run")], []),
        };
    }

    // The files under packages/<id>/ of each package, with the path inside the package. A file directly under packages/ is not a package's.
    private static IEnumerable<IGrouping<string, (string[] Segments, string Content)>> PackageFiles(IReadOnlyList<ResourceContent> files) => files
        .Where(file => file.Path.StartsWith(ApplicationLoader.PackagesFolderPrefix, StringComparison.Ordinal))
        .Select(file => (Segments: file.Path[ApplicationLoader.PackagesFolderPrefix.Length..].Split('/', 2), file.Content))
        .Where(file => file.Segments.Length == 2)
        .GroupBy(file => file.Segments[0], StringComparer.Ordinal);

    private static IEnumerable<Diagnostic> Refusals(string package, RefusedMessage refused) => refused.Problems.Select(problem => new Diagnostic(
        DiagnosticCodes.ApplicationPackageBuildRefused,
        $"Package '{package}': {problem.Message}",
        $"{ApplicationLoader.PackagesFolderPrefix}{package}/{problem.File}",
        ""));

    private static Diagnostic NoAnswer(string package, string verb, string consequence) => new(
        DiagnosticCodes.ApplicationPackageBuildNoAnswer,
        $"Package '{package}': The package host did not answer its {verb}, so {consequence}. Ask the operator to check application-package-host.",
        $"{ApplicationLoader.PackagesFolderPrefix}{package}/{PackageCatalog.ManifestFileName}",
        "");

    // Axis builds and scans nothing of a customer-hosted package, which holds its manifest alone.
    private static bool IsCustomerHosted(IEnumerable<(string[] Segments, string Content)> package) =>
        package.Any(file => file.Segments[1] == PackageCatalog.ManifestFileName && PackageCatalog.IsCustomerHosted(file.Content));
}

/// <summary>The diagnostics of the builds of a snapshot's packages, and the files of the packages that were built.</summary>
internal sealed record PackageBuildResult(IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyDictionary<string, byte[]> Files);

/// <summary>The diagnostics of the test of one package and the outcome of each test that ran. A refused or unanswered test has no outcome.</summary>
internal sealed record PackageTestResult(IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<PackageTestOutcome> Tests);
