namespace Axis.Tenancy.Platform;

/// <summary>
/// The field names that <see cref="TenantSecretCipher"/> binds into an encrypted value. They are part of the stored
/// format, so a name never changes.
/// </summary>
public static class TenantSecretFields
{
    public const string RuntimePassword = "runtime_password";

    public const string SchemaPassword = "schema_password";

    public const string OidcClientSecret = "oidc_client_secret";

    /// <summary>The field of a client key for <c>private_key_jwt</c>. The current and the previous key share it.</summary>
    public const string OidcClientKey = "oidc_client_key";

    /// <summary>The field of a tenant's own SMTP password.</summary>
    public const string SmtpPassword = "smtp_password";

    /// <summary>The field of the secret of the tenant realm's client that activation uses to change providers and clients.</summary>
    public const string RealmActivationClientSecret = "realm_activation_client_secret";

    /// <summary>The field of the secret of the tenant realm's client that the users page uses to change users.</summary>
    public const string RealmUsersClientSecret = "realm_users_client_secret";

    /// <summary>The field of a data protection key. It is bound with an empty tenant id.</summary>
    public static string DataProtectionKey(Guid id) => "data_protection_key:" + id.ToString("D");

    /// <summary>The field of a <c>secret</c> binding. The name is lower case, because binding names ignore letter case.</summary>
    public static string Binding(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return "binding:" + name.ToLowerInvariant();
    }
}
