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

    /// <summary>The field of a <c>secret</c> binding. The name is lower case, because binding names ignore letter case.</summary>
    public static string Binding(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return "binding:" + name.ToLowerInvariant();
    }
}
