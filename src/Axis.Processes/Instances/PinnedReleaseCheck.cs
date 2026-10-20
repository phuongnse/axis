using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Processes.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// The start check of the server and the worker: every release a tenant still uses must find the
/// extension package versions it pins installed. A release is in use when it is active or when a
/// running or waiting process instance is pinned to it.
/// </summary>
public static class PinnedReleaseCheck
{
    /// <summary>
    /// Checks each release in use in the tenant database behind <paramref name="connection"/>
    /// against <paramref name="installed"/>, in order of release id. A pinned version that is not
    /// installed is <c>AXC0146</c>, reported at the release's stored <c>application.json</c>. A
    /// release with every pin installed is rebuilt from its stored files. Returns
    /// <see langword="null"/> without checking when the configuration or processes migrations are
    /// still pending, as the tables to read do not exist yet.
    /// </summary>
    /// <exception cref="InvalidOperationException">A release in use no longer compiles against the installed packages.</exception>
    public static async Task<IReadOnlyList<Diagnostic>?> CheckAsync(
        NpgsqlConnection connection,
        string tenantId,
        IReadOnlyCollection<PackageVersion> installed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(installed);

        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
        await using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connection).Options);
        if ((await configuration.Database.GetPendingMigrationsAsync(cancellationToken)).Any()
            || (await processes.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            return null;
        }

        var active = await configuration.ActiveReleases.AsNoTracking().Select(row => row.ReleaseId).ToListAsync(cancellationToken);
        var open = await processes.Instances
            .AsNoTracking()
            .Where(instance => instance.State == ProcessStarts.Running || instance.State == ProcessStarts.Waiting)
            .Select(instance => instance.ReleaseId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var store = new ActiveReleaseStore(configuration);
        var diagnostics = new List<Diagnostic>();
        foreach (var releaseId in active.Concat(open).Distinct().Order())
        {
            // A release is never deleted, so a missing row means a broken database.
            var release = await store.GetReleaseAsync(releaseId, cancellationToken)
                ?? throw new InvalidOperationException($"The release '{releaseId}' could not be found.");
            var missing = MissingPins(release, tenantId, installed);
            diagnostics.AddRange(missing);
            if (missing.Count == 0)
            {
                ReleaseCompiler.BuildModel(release, installed);
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// The <c>AXC0146</c> diagnostics of <paramref name="release"/>: one for each pinned package version that is not in
    /// <paramref name="installed"/>, in the order of the release's pins. Empty when every pin is installed.
    /// </summary>
    public static IReadOnlyList<Diagnostic> MissingPins(Release release, string tenantId, IReadOnlyCollection<PackageVersion> installed)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(installed);

        return [.. release.Packages
            .Where(pin => !installed.Any(package => string.Equals(package.Id, pin.PackageId, StringComparison.Ordinal)
                && string.Equals(package.Version, pin.Version, StringComparison.Ordinal)))
            .Select(pin => Missing(release, pin, tenantId))];
    }

    private static Diagnostic Missing(Release release, ReleasePackage pin, string tenantId)
    {
        string? name = null;
        var index = -1;
        var manifest = release.Resources.FirstOrDefault(resource => string.Equals(resource.Path, ApplicationLoader.ManifestFileName, StringComparison.Ordinal));
        if (manifest is not null)
        {
            using var document = JsonDocument.Parse(manifest.Content);
            if (document.RootElement.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                name = nameElement.GetString();
            }

            if (document.RootElement.TryGetProperty("packages", out var packages) && packages.ValueKind == JsonValueKind.Array)
            {
                var position = 0;
                foreach (var entry in packages.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object
                        && entry.TryGetProperty("id", out var id)
                        && id.ValueKind == JsonValueKind.String
                        && string.Equals(id.GetString(), pin.PackageId, StringComparison.Ordinal))
                    {
                        index = position;
                        break;
                    }

                    position++;
                }
            }
        }

        return new Diagnostic(
            DiagnosticCodes.PinnedPackageVersionMissing,
            $"Release '{release.Id}' of application '{name}' for tenant '{tenantId}' pins package '{pin.PackageId}' version '{pin.Version}', which is not installed.",
            ApplicationLoader.ManifestFileName,
            index < 0 ? string.Empty : $"/packages/{index}/version");
    }
}
