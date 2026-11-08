using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy;

/// <summary>
/// Whether a tenant administrator may set the tenant's own model key, see
/// <c>docs/reference/model-bindings.md#a-tenants-own-key</c>. <c>Ai:AllowTenantKeys</c> is off by default.
/// </summary>
/// <param name="Allowed">True when a tenant administrator may set, change and remove the tenant's model key.</param>
public sealed record TenantModelKeySettings(bool Allowed)
{
    public const string AllowSetting = "Ai:AllowTenantKeys";

    /// <summary>Reads the setting. A blank value counts as unset. A value other than <c>true</c> or <c>false</c> stops the start, and the message never holds the value.</summary>
    public static TenantModelKeySettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var text = configuration[AllowSetting];
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TenantModelKeySettings(false);
        }

        return bool.TryParse(text, out var allowed)
            ? new TenantModelKeySettings(allowed)
            : throw new InvalidOperationException($"'{AllowSetting}' must be true or false.");
    }
}
