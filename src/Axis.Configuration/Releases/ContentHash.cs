using System.Security.Cryptography;
using System.Text;

namespace Axis.Configuration.Releases;

/// <summary>
/// The content hash of an application: SHA-256 over every resource in ordinal path order, each
/// written as its path, a newline, its canonical JSON and a newline, encoded as UTF-8. Canonical
/// JSON escapes every control character, so the newlines cannot be confused with content.
/// </summary>
public static class ContentHash
{
    public static string Compute(IReadOnlyList<ResourceContent> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var resource in resources.OrderBy(resource => resource.Path, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes($"{resource.Path}\n{resource.Content}\n"));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
