namespace Axis.ControlPlane.SignUp;

/// <summary>The address of a sign-up's tenant, shown in the confirm email, the trial-used email and on the verify page, see <c>docs/reference/sign-up.md#the-verify-page</c>.</summary>
internal static class SignUpAddress
{
    public static string Of(ControlPlaneOptions options, string subdomain) => $"https://{subdomain}.{options.TenantDomain!.ToLowerInvariant()}";

    /// <summary>The billing portal link of a tenant, shown in the timeline emails and the trial-used email.</summary>
    public static string BillingPortalOf(ControlPlaneOptions options, string tenantId) => $"{options.PublicUrl}/tenants/{Uri.EscapeDataString(tenantId)}";
}
