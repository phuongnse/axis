namespace Axis.ControlPlane.SignUp;

/// <summary>The address of a sign-up's tenant, shown in the confirm email, the trial-used email, on the verify page and in the progress route, see <c>docs/reference/sign-up.md#the-verify-page</c>.</summary>
internal static class SignUpAddress
{
    public static string Of(ControlPlaneOptions options, string subdomain) => $"https://{subdomain}.{options.TenantDomain!.ToLowerInvariant()}";
}
