using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Packages;

/// <summary>The outcome of <see cref="PackageCatalog.CheckFolder"/>: the package when it has no problem, otherwise its problems.</summary>
public sealed record PackageFolderCheck(InstalledPackage? Package, IReadOnlyList<Diagnostic> Diagnostics);
