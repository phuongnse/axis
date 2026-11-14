using System.Security.Cryptography;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>phonenumbers</c> wheel in the wheel cache that <c>scripts/setup.sh</c> fills, so a test installs the sample
/// Python package's dependency with pip and no package index.
/// </summary>
internal static class PhoneNumbersWheel
{
    private const string Wheel = "phonenumbers-9.0.43-";

    /// <summary>The absolute path of the wheel in the cache.</summary>
    public static string Path()
    {
        var cache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "axis-tools", "wheels");
        var wheels = Directory.Exists(cache) ? Directory.GetFiles(cache, $"{Wheel}*.whl") : [];
        return wheels.Length > 0
            ? wheels[0]
            : throw new InvalidOperationException($"The wheel '{Wheel}*.whl' is not in '{cache}'. Run scripts/setup.sh to fill the wheel cache.");
    }

    /// <summary>A <c>requirements.txt</c> line that locks the wheel by its absolute path and its SHA-256 hash.</summary>
    public static string Requirement() =>
        $"{Path()} --hash=sha256:{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path())))}\n";
}
