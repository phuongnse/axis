using System.Security.Cryptography;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Admin;

/// <summary>
/// The backup key of each tenant of a cell: 32 random bytes in <c>platform.tenant_backup_keys</c>, encrypted by the cell's backup key
/// (<c>Cell:BackupKeyFile</c>) and bound to the tenant id. <c>tenant create</c> makes it, and <c>tenant backup</c> and
/// <c>tenant restore</c> read it in place of <c>--backup-key-file</c>.
/// </summary>
internal static class TenantBackupKeys
{
    public const string Setting = "Cell:BackupKeyFile";

    private const int KeyBytes = 32;

    /// <summary>
    /// Reads the cell's backup key from the file that <c>Cell:BackupKeyFile</c> names. Returns null with the reason in
    /// <paramref name="problem"/> when the setting is blank or the file cannot be read or does not hold a key.
    /// </summary>
    public static TenantSecretCipher? ReadCellCipher(CellOptions cell, out string problem)
    {
        if (string.IsNullOrWhiteSpace(cell.BackupKeyFile))
        {
            problem = $"The setting {Setting} is required.";
            return null;
        }

        var key = BackupKeyFile.Read(cell.BackupKeyFile, out problem, Setting);
        return key is null ? null : TenantSecretCipher.FromKey(key);
    }

    /// <summary>
    /// Adds the tenant's backup key when it has none, and leaves the one it has. It does not save, so the caller saves the row
    /// with its own change. The tenant row must be added or saved before the save.
    /// </summary>
    public static async Task AddMissingAsync(PlatformDbContext platform, string tenantId, TenantSecretCipher cell, CancellationToken cancellationToken)
    {
        if (await platform.TenantBackupKeys.AnyAsync(key => key.TenantId == tenantId, cancellationToken))
        {
            return;
        }

        platform.TenantBackupKeys.Add(new TenantBackupKeyRow
        {
            TenantId = tenantId,
            Key = cell.Encrypt(tenantId, TenantSecretFields.BackupKey, Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes))),
        });
    }

    /// <summary>Returns the tenant's backup key, or null when it has none.</summary>
    /// <exception cref="InvalidOperationException">The stored key does not unwrap with the cell's key.</exception>
    public static async Task<byte[]?> ReadAsync(string platformConnection, string tenantId, TenantSecretCipher cell, CancellationToken cancellationToken)
    {
        await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnection).Options);
        var stored = await platform.TenantBackupKeys.AsNoTracking()
            .Where(key => key.TenantId == tenantId)
            .Select(key => key.Key)
            .FirstOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(cell.Decrypt(tenantId, TenantSecretFields.BackupKey, stored).Reveal());
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException($"The backup key of tenant '{tenantId}' does not unwrap with the key of {Setting}.", exception);
        }
    }
}
