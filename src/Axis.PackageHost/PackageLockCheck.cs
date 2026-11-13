using System.Text.Json;
using Axis.Configuration.Packages;

namespace Axis.PackageHost;

/// <summary>
/// Checks the lock file of a package, see docs/reference/extension-packages.md#building-a-package. For a C#
/// package it is checked against the runtime dependency list that the build published beside the assembly: the
/// lock file is stale when that list names a NuGet package that the lock file does not resolve at the same
/// version, and a lock file entry with no runtime asset, such as an analyzer, is allowed. For a Node.js package
/// it is checked against <c>package.json</c>: the root entry of <c>package-lock.json</c> must list the same
/// dependencies at the same ranges.
/// </summary>
internal static class PackageLockCheck
{
    /// <summary>The problems with the lock file of the package in <paramref name="source"/>. None means it is current.</summary>
    public static IReadOnlyList<string> Check(string source, PackageManifest manifest) =>
        manifest.Language == PackageLanguage.Node ? CheckNode(source, manifest) : CheckCSharp(source, manifest);

    private static IReadOnlyList<string> CheckCSharp(string source, PackageManifest manifest)
    {
        var lockFile = Path.Combine(source, manifest.Lock);
        if (!File.Exists(lockFile))
        {
            return [$"The lock file '{manifest.Lock}' is missing."];
        }

        var depsName = Path.ChangeExtension(manifest.Assembly!, ".deps.json");
        var depsFile = Path.Combine(source, depsName);
        if (!File.Exists(depsFile))
        {
            return [$"'{manifest.Lock}' cannot be checked: the runtime dependency list '{depsName}' is missing."];
        }

        HashSet<string> locked;
        List<string> libraries;
        try
        {
            locked = ReadLocked(lockFile);
            libraries = ReadLibraries(depsFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // The exception message names the absolute path, so it is never passed on.
            return [$"'{manifest.Lock}' cannot be checked: it or '{depsName}' cannot be read as JSON."];
        }

        return
        [
            .. libraries
                .Where(library => !locked.Contains(library))
                .Select(library => $"'{manifest.Lock}' is stale: it does not resolve {library.Replace('/', ' ')}, which {depsName} lists."),
        ];
    }

    private static readonly string[] _nodeDependencyKinds = ["dependencies", "devDependencies", "optionalDependencies", "peerDependencies"];

    private static List<string> CheckNode(string source, PackageManifest manifest)
    {
        var lockFile = Path.Combine(source, manifest.Lock);
        if (!File.Exists(lockFile))
        {
            return [$"The lock file '{manifest.Lock}' is missing."];
        }

        var packageFile = Path.Combine(source, "package.json");
        if (!File.Exists(packageFile))
        {
            return [$"'{manifest.Lock}' cannot be checked: 'package.json' is missing."];
        }

        try
        {
            using var packageDocument = JsonDocument.Parse(File.ReadAllText(packageFile));
            using var lockDocument = JsonDocument.Parse(File.ReadAllText(lockFile));
            if (packageDocument.RootElement.ValueKind != JsonValueKind.Object
                || lockDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [$"'{manifest.Lock}' cannot be checked: it or 'package.json' is not a JSON object."];
            }

            if (!lockDocument.RootElement.TryGetProperty("packages", out var packages)
                || packages.ValueKind != JsonValueKind.Object
                || !packages.TryGetProperty("", out var lockRoot)
                || lockRoot.ValueKind != JsonValueKind.Object)
            {
                return [$"'{manifest.Lock}' cannot be checked: it has no root entry for 'package.json'."];
            }

            return Differences(manifest.Lock, packageDocument.RootElement, lockRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // The exception message names the absolute path, so it is never passed on.
            return [$"'{manifest.Lock}' cannot be checked: it or 'package.json' cannot be read as JSON."];
        }
    }

    /// <summary>Each dependency that <c>package.json</c> and the root entry of the lock file do not list alike, in both directions.</summary>
    private static List<string> Differences(string lockName, JsonElement package, JsonElement lockRoot)
    {
        var differences = new List<string>();
        foreach (var kind in _nodeDependencyKinds)
        {
            var wanted = ReadRanges(package, kind);
            var locked = ReadRanges(lockRoot, kind);
            foreach (var (name, range) in wanted)
            {
                if (!locked.TryGetValue(name, out var lockedRange))
                {
                    differences.Add($"'{lockName}' does not match package.json: it does not lock {name} {range}, which {kind} lists.");
                }
                else if (lockedRange != range)
                {
                    differences.Add($"'{lockName}' does not match package.json: it locks {name} {lockedRange}, but {kind} lists {range}.");
                }
            }

            differences.AddRange(
                locked.Keys.Where(name => !wanted.ContainsKey(name))
                    .Select(name => $"'{lockName}' does not match package.json: it locks {name} {locked[name]}, which {kind} does not list."));
        }

        return differences;
    }

    /// <summary>The name and range of each entry of the object <paramref name="kind"/> of an element, or none when it has no such object.</summary>
    private static SortedDictionary<string, string> ReadRanges(JsonElement element, string kind)
    {
        var ranges = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (element.TryGetProperty(kind, out var entries) && entries.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in entries.EnumerateObject())
            {
                ranges[entry.Name] = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString()! : entry.Value.GetRawText();
            }
        }

        return ranges;
    }

    /// <summary>The <c>Name/Version</c> of each package that the lock file resolves, in lower case.</summary>
    private static HashSet<string> ReadLocked(string lockFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(lockFile));
        var locked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.TryGetProperty("dependencies", out var frameworks))
        {
            foreach (var framework in frameworks.EnumerateObject())
            {
                foreach (var dependency in framework.Value.EnumerateObject())
                {
                    if (dependency.Value.TryGetProperty("resolved", out var resolved)
                        && !(dependency.Value.TryGetProperty("type", out var type) && type.GetString() == "Project"))
                    {
                        locked.Add($"{dependency.Name}/{resolved.GetString()}");
                    }
                }
            }
        }

        return locked;
    }

    /// <summary>The <c>Name/Version</c> key of each library of type <c>package</c> in the runtime dependency list.</summary>
    private static List<string> ReadLibraries(string depsFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(depsFile));
        var libraries = new List<string>();
        if (document.RootElement.TryGetProperty("libraries", out var entries))
        {
            libraries.AddRange(
                entries.EnumerateObject()
                    .Where(entry => entry.Value.TryGetProperty("type", out var type) && type.GetString() == "package")
                    .Select(entry => entry.Name));
        }

        return libraries;
    }
}
