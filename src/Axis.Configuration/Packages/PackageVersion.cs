namespace Axis.Configuration.Packages;

/// <summary>An installed version of an extension package, by id and <c>major.minor.patch</c> version.</summary>
public sealed record PackageVersion(string Id, string Version);
