using Axis.Configuration.Model;

namespace Axis.Data;

/// <summary>
/// One deployment binding of a tenant as activation sees it: its <see cref="Kind"/>, <c>url</c>,
/// <c>secret</c> or <c>storage</c>, and for a <c>url</c> binding its <see cref="Url"/>. A secret's value is never
/// passed in.
/// </summary>
public sealed record ActivationBinding(string Kind, Uri? Url)
{
    public const string UrlKind = "url";
    public const string SecretKind = "secret";
    public const string StorageKind = "storage";
}

/// <summary>
/// The deployment bindings of the tenant a release is activated for, keyed by binding name and
/// matched ignoring letter case, and whether a <c>url</c> binding may use <c>http</c>, as it may
/// only in Development. On a preview tenant, <paramref name="IsPreview"/>, a missing binding is an activation warning
/// and not an error. <see cref="PlatformProviders"/> are the brokered providers of the tenant's platform sign-in, which the realm plan
/// keeps and shares with the providers of the applications.
/// </summary>
public sealed record ActivationBindings(string TenantId, IReadOnlyDictionary<string, ActivationBinding> Bindings, bool AllowHttp, bool IsPreview = false)
{
    /// <summary>The brokered providers of the tenant's platform sign-in. Empty when it has none.</summary>
    public IReadOnlyList<IdentityProviderModel> PlatformProviders { get; init; } = [];

    /// <summary>A tenant with no bindings, for an application without connectors.</summary>
    public static ActivationBindings None(string tenantId) =>
        new(tenantId, new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase), AllowHttp: false);
}
