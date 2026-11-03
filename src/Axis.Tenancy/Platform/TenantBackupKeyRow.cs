namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.tenant_backup_keys</c>: the backup key of one tenant of a cell. <see cref="Key"/> holds the base64 text of
/// 32 random bytes encrypted by <see cref="TenantSecretCipher"/> with the field <see cref="TenantSecretFields.BackupKey"/>, keyed with
/// the cell's backup key and not the installation key, never the plain key.
/// </summary>
public sealed class TenantBackupKeyRow
{
    public string TenantId { get; set; } = "";

    public byte[] Key { get; set; } = [];
}
