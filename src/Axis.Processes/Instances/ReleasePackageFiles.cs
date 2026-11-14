using System.Security.Cryptography;
using System.Text;
using Axis.Configuration.Loading;
using Axis.Configuration.Packages;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Reads the files of an application's own package from a release over a connection to a tenant database, see
/// docs/reference/extension-packages.md#an-applications-packages. A published release stores the built files under
/// <c>packages/&lt;id&gt;/</c> in <c>axis.release_package_files</c> and the package's source in <c>axis.release_resources</c>.
/// </summary>
internal static class ReleasePackageFiles
{
    /// <summary>
    /// Loads the built files of the package <paramref name="packageId"/> of <paramref name="releaseId"/>, each keyed by its path
    /// relative to the package's folder. <c>HasSource</c> is true when the release holds the package's manifest as source, which is
    /// only asked when it has no built file.
    /// </summary>
    public static async Task<(bool HasSource, Dictionary<string, byte[]> Files)> LoadAsync(
        NpgsqlTransaction transaction,
        Guid releaseId,
        string packageId,
        CancellationToken cancellationToken)
    {
        var prefix = $"{ApplicationLoader.PackagesFolderPrefix}{packageId}/";
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand(
            "SELECT substr(path, length(@prefix) + 1), content FROM axis.release_package_files WHERE release_id = @release AND starts_with(path, @prefix)",
            transaction.Connection,
            transaction))
        {
            command.Parameters.Add(new NpgsqlParameter("prefix", NpgsqlDbType.Text) { Value = prefix });
            command.Parameters.AddWithValue("release", releaseId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                files[reader.GetString(0)] = (byte[])reader[1];
            }
        }

        if (files.Count > 0)
        {
            return (true, files);
        }

        await using var source = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM axis.release_resources WHERE release_id = @release AND path = @manifest)",
            transaction.Connection,
            transaction);
        source.Parameters.AddWithValue("release", releaseId);
        source.Parameters.Add(new NpgsqlParameter("manifest", NpgsqlDbType.Text) { Value = prefix + PackageCatalog.ManifestFileName });
        return ((bool)(await source.ExecuteScalarAsync(cancellationToken))!, files);
    }

    /// <summary>
    /// The SHA-256 of <paramref name="files"/> as lower-case hex: every file in ordinal path order, each written as its path, a newline, its
    /// length in bytes, a newline, its bytes and a newline. The length keeps a path from being confused with content, as the raw-text
    /// rule of <see cref="Axis.Configuration.Releases.ContentHash"/> does for source files. Two releases that hold the same built files give the same digest.
    /// </summary>
    public static string Digest(IReadOnlyDictionary<string, byte[]> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var newline = "\n"u8;
        foreach (var (path, content) in files.OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes($"{path}\n{content.Length}\n"));
            hash.AppendData(content);
            hash.AppendData(newline);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
