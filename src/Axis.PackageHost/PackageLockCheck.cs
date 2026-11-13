using System.Text.Json;
using Axis.Configuration.Packages;

namespace Axis.PackageHost;

/// <summary>
/// Checks the lock file of a C# package against the runtime dependency list that the build published beside
/// the assembly, see docs/reference/extension-packages.md#building-a-package. The lock file is stale when that
/// list names a NuGet package that the lock file does not resolve at the same version. A lock file entry with no
/// runtime asset, such as an analyzer, is allowed.
/// </summary>
internal static class PackageLockCheck
{
    /// <summary>The problems with the lock file of the package in <paramref name="source"/>. None means it is current.</summary>
    public static IReadOnlyList<string> Check(string source, PackageManifest manifest)
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
