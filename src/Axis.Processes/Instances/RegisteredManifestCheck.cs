using System.Text.Json;
using Axis.Configuration.Loading;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Processes.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>What <see cref="RegisteredManifestCheck"/> found.</summary>
public enum RegisteredManifestOutcome
{
    /// <summary>At least one release in use pins the version as customer-hosted, and every such pin is equal.</summary>
    Equal,

    /// <summary>No release in use pins the version as customer-hosted.</summary>
    NotPinned,

    /// <summary>A release in use pins the version as customer-hosted, and its manifest differs.</summary>
    Different,
}

/// <summary>
/// Compares the manifest that a customer-hosted runner registers with the manifests that the releases in use pin, see
/// docs/reference/extension-packages.md#customer-hosted-runners. A release is in use when it is active or has a running or
/// waiting process instance, as <see cref="PinnedReleaseCheck"/> counts it.
/// </summary>
public static class RegisteredManifestCheck
{
    private static readonly string[] _compared = ["id", "version", "operations"];

    /// <summary>
    /// Checks <paramref name="manifest"/> against each release in use in the tenant database behind <paramref name="connection"/>.
    /// A pin counts when it is the application's own package at that version with <c>"runner": "customer"</c>, or an installed
    /// package that the release's <c>packages</c> entry pins with <c>"runner": "customer"</c>. Only <c>id</c>, <c>version</c> and
    /// <c>operations</c> are compared, as JSON values, so the order of properties and whitespace do not matter. The manifest
    /// of an installed pin is its file in <paramref name="installed"/>, and a pin whose file cannot be read is different.
    /// </summary>
    /// <exception cref="InvalidOperationException">A release in use is missing or no longer compiles against the installed packages.</exception>
    public static async Task<RegisteredManifestOutcome> CheckAsync(
        NpgsqlConnection connection,
        JsonElement manifest,
        PackageCatalog installed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(installed);

        if (!TryReadString(manifest, "id", out var id) || !TryReadString(manifest, "version", out var version))
        {
            return RegisteredManifestOutcome.NotPinned;
        }

        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
        await using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connection).Options);
        var store = new ActiveReleaseStore(configuration);
        var pinned = false;
        foreach (var releaseId in await ReleasesInUse.ListAsync(configuration, processes, cancellationToken))
        {
            // A release is never deleted, so a missing row means a broken database.
            var release = await store.GetReleaseAsync(releaseId, cancellationToken)
                ?? throw new InvalidOperationException($"The release '{releaseId}' could not be found.");

            if (OwnPackageManifest(release, id, version) is { } own)
            {
                pinned = true;
                if (!Equal(manifest, own))
                {
                    return RegisteredManifestOutcome.Different;
                }
            }

            if (PinsInstalledCustomerHosted(release, id, version, installed))
            {
                pinned = true;
                if (installed.ReadManifestJson(id, version) is not { } file || !Equal(manifest, file))
                {
                    return RegisteredManifestOutcome.Different;
                }
            }
        }

        return pinned ? RegisteredManifestOutcome.Equal : RegisteredManifestOutcome.NotPinned;
    }

    /// <summary>The manifest of the release's own package <paramref name="id"/> when it is customer-hosted at <paramref name="version"/>.</summary>
    private static JsonElement? OwnPackageManifest(Release release, string id, string version)
    {
        var path = $"{ApplicationLoader.PackagesFolderPrefix}{id}/{PackageCatalog.ManifestFileName}";
        var resource = release.Resources.FirstOrDefault(candidate => string.Equals(candidate.Path, path, StringComparison.Ordinal));
        if (resource is null || !PackageCatalog.IsCustomerHosted(resource.Content))
        {
            return null;
        }

        using var document = JsonDocument.Parse(resource.Content);
        var root = document.RootElement.Clone();
        return TryReadString(root, "version", out var own) && string.Equals(own, version, StringComparison.Ordinal) ? root : null;
    }

    /// <summary>
    /// True when the release pins the installed package version and its <c>packages</c> entry says <c>"runner": "customer"</c>.
    /// The compiler settles the flag, so the model is rebuilt only for a release that pins the version.
    /// </summary>
    private static bool PinsInstalledCustomerHosted(Release release, string id, string version, PackageCatalog installed) =>
        release.Packages.Any(pin => string.Equals(pin.PackageId, id, StringComparison.Ordinal) && string.Equals(pin.Version, version, StringComparison.Ordinal))
        && ReleaseCompiler.BuildModel(release, installed.Versions).Packages.Any(package =>
            package.CustomerHosted
            && string.Equals(package.Id, id, StringComparison.Ordinal)
            && string.Equals(package.Version, version, StringComparison.Ordinal));

    private static bool Equal(JsonElement registered, JsonElement pinned) =>
        _compared.All(property =>
            registered.ValueKind == JsonValueKind.Object
            && pinned.ValueKind == JsonValueKind.Object
            && registered.TryGetProperty(property, out var left)
            && pinned.TryGetProperty(property, out var right)
            && JsonElement.DeepEquals(left, right));

    private static bool TryReadString(JsonElement manifest, string property, out string value)
    {
        if (manifest.ValueKind == JsonValueKind.Object
            && manifest.TryGetProperty(property, out var element)
            && element.ValueKind == JsonValueKind.String
            && element.GetString() is { } text)
        {
            value = text;
            return true;
        }

        value = "";
        return false;
    }
}
