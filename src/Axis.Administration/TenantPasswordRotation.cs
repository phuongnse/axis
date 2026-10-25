using System.Security.Cryptography;
using Axis.Tenancy.Platform;
using Npgsql;

namespace Axis.Administration;

/// <summary>
/// Gives a tenant's database roles new generated passwords. The command line and a later web console both call it, so a
/// rotation follows the same rules everywhere. Each role is one step: the new password is saved encrypted and the tenant
/// <c>version</c> is raised in a platform transaction, <c>ALTER ROLE</c> runs over the admin connection, and the transaction
/// commits only when <c>ALTER ROLE</c> succeeded. A failed step leaves the stored password as it was.
/// </summary>
public static class TenantPasswordRotation
{
    /// <summary>Rotates the password of the tenant's runtime role.</summary>
    public static Task RotateRuntimeAsync(
        TenantSecretCipher cipher,
        string platformConnectionString,
        NpgsqlConnection admin,
        TenantRow tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return RotateAsync(cipher, platformConnectionString, admin, tenant, tenant.RuntimeUser, TenantSecretFields.RuntimePassword, "runtime_password", cancellationToken);
    }

    /// <summary>Rotates the password of the tenant's schema role.</summary>
    public static Task RotateSchemaAsync(
        TenantSecretCipher cipher,
        string platformConnectionString,
        NpgsqlConnection admin,
        TenantRow tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return RotateAsync(cipher, platformConnectionString, admin, tenant, tenant.SchemaUser, TenantSecretFields.SchemaPassword, "schema_password", cancellationToken);
    }

    private static async Task RotateAsync(
        TenantSecretCipher cipher,
        string platformConnectionString,
        NpgsqlConnection admin,
        TenantRow tenant,
        string role,
        string field,
        string column,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentException.ThrowIfNullOrWhiteSpace(platformConnectionString);
        ArgumentNullException.ThrowIfNull(admin);

        // Hex needs no escaping in the statement, which cannot take a parameter.
        var password = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        await using var platform = new NpgsqlConnection(platformConnectionString);
        await platform.OpenAsync(cancellationToken);
        await using var transaction = await platform.BeginTransactionAsync(cancellationToken);

        await using (var save = new NpgsqlCommand($"UPDATE platform.tenants SET {column} = @password, version = version + 1 WHERE id = @id", platform, transaction))
        {
            save.Parameters.AddWithValue("password", cipher.Encrypt(tenant.Id, field, password));
            save.Parameters.AddWithValue("id", tenant.Id);
            if (await save.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The tenant does not exist.");
            }
        }

        // A failure here leaves the transaction uncommitted, so it rolls back and the stored password stays as it was.
        await using (var alter = new NpgsqlCommand($"ALTER ROLE {Quote(role)} PASSWORD '{password}'", admin))
        {
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
