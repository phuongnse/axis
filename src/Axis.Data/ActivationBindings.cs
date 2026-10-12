namespace Axis.Data;

/// <summary>
/// One deployment binding of a tenant as activation sees it: its <see cref="Kind"/>, <c>url</c> or
/// <c>secret</c>, and for a <c>url</c> binding its <see cref="Url"/>. A secret's value is never
/// passed in.
/// </summary>
public sealed record ActivationBinding(string Kind, Uri? Url)
{
    public const string UrlKind = "url";
    public const string SecretKind = "secret";
}

/// <summary>
/// The deployment bindings of the tenant a release is activated for, keyed by binding name and
/// matched ignoring letter case, and whether a <c>url</c> binding may use <c>http</c>, as it may
/// only in Development.
/// </summary>
public sealed record ActivationBindings(string TenantId, IReadOnlyDictionary<string, ActivationBinding> Bindings, bool AllowHttp)
{
    /// <summary>A tenant with no bindings, for an application without connectors.</summary>
    public static ActivationBindings None(string tenantId) =>
        new(tenantId, new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase), AllowHttp: false);
}
