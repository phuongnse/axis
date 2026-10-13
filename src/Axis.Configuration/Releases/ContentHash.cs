using System.Security.Cryptography;
using System.Text;
using Axis.Configuration.Model;

namespace Axis.Configuration.Releases;

/// <summary>
/// The content hash of an application: SHA-256 over every resource in ordinal path order, each
/// written as its path, a newline, its canonical JSON and a newline, then over every pinned
/// extension package in ordinal id order, each written as <c>/packages/</c> and its id, a newline,
/// its version and a newline, all encoded as UTF-8. Canonical JSON escapes every control character,
/// so the newlines cannot be confused with content. A resource path never starts with <c>/</c>, so
/// a pin cannot be confused with a resource. An application without pins keeps the hash of its
/// resources alone.
/// </summary>
public static class ContentHash
{
    public static string Compute(IReadOnlyList<ResourceContent> resources) => Compute(resources, []);

    public static string Compute(IReadOnlyList<ResourceContent> resources, IReadOnlyList<PinnedPackageModel> packages)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(packages);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var resource in resources.OrderBy(resource => resource.Path, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes($"{resource.Path}\n{resource.Content}\n"));
        }

        foreach (var package in packages.OrderBy(package => package.Id, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes($"/packages/{package.Id}\n{package.Version}\n"));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
