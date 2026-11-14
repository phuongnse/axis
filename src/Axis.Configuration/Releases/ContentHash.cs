using System.Security.Cryptography;
using System.Text;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;

namespace Axis.Configuration.Releases;

/// <summary>
/// The content hash of an application: SHA-256 over every resource in ordinal path order, each
/// written as its path, a newline, its canonical JSON and a newline, then over every pinned
/// extension package in ordinal id order, each written as <c>/packages/</c> and its id, a newline,
/// its version and a newline, all encoded as UTF-8. A file of an application's own package, under
/// <c>packages/</c>, is raw text, not canonical JSON, so it is written as its path, a newline, its
/// length in UTF-8 bytes, a newline, its text as written and a newline. Canonical JSON escapes every
/// control character, so its newlines cannot be confused with content, and the length does the same
/// for raw text. A resource path never starts with <c>/</c>, so a pin cannot be confused with a
/// resource. An application without pins or package files keeps the hash it had before they existed.
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
            hash.AppendData(Encoding.UTF8.GetBytes(resource.Path.StartsWith(ApplicationLoader.PackagesFolderPrefix, StringComparison.Ordinal)
                ? $"{resource.Path}\n{Encoding.UTF8.GetByteCount(resource.Content)}\n{resource.Content}\n"
                : $"{resource.Path}\n{resource.Content}\n"));
        }

        foreach (var package in packages.OrderBy(package => package.Id, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes($"/packages/{package.Id}\n{package.Version}\n"));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
