using Axis.Data;
using Axis.Tenancy;
using Axis.Tenancy.Platform;

namespace Axis.Integration.Tests;

/// <summary>The Company sign-in bindings that a test tenant gives, as configuration, activation bindings or directory rows.</summary>
internal static class CompanyBindings
{
    public const string Issuer = "CompanyIssuer";

    public const string ClientId = "CompanyClientId";

    public const string ClientSecret = "CompanyClientSecret";

    public const string IssuerUrl = "https://company.example.test/realms/company";

    private const string ClientIdValue = "axis-broker";

    private const string ClientSecretValue = "axis-broker-development-secret";

    public static IEnumerable<KeyValuePair<string, string?>> Settings(string tenant) =>
    [
        new($"Tenants:{tenant}:Bindings:{Issuer}:Kind", DeploymentBindingKinds.Url),
        new($"Tenants:{tenant}:Bindings:{Issuer}:Value", IssuerUrl),
        new($"Tenants:{tenant}:Bindings:{ClientId}:Kind", DeploymentBindingKinds.Secret),
        new($"Tenants:{tenant}:Bindings:{ClientId}:Value", ClientIdValue),
        new($"Tenants:{tenant}:Bindings:{ClientSecret}:Kind", DeploymentBindingKinds.Secret),
        new($"Tenants:{tenant}:Bindings:{ClientSecret}:Value", ClientSecretValue),
    ];

    public static IReadOnlyList<TenantBindingRow> Rows(string tenantId) =>
    [
        new() { TenantId = tenantId, Name = Issuer, Kind = DeploymentBindingKinds.Url, Url = IssuerUrl },
        new()
        {
            TenantId = tenantId,
            Name = ClientId,
            Kind = DeploymentBindingKinds.Secret,
            SecretValue = AdminScenario.Cipher.Encrypt(tenantId, TenantSecretFields.Binding(ClientId), ClientIdValue),
        },
        new()
        {
            TenantId = tenantId,
            Name = ClientSecret,
            Kind = DeploymentBindingKinds.Secret,
            SecretValue = AdminScenario.Cipher.Encrypt(tenantId, TenantSecretFields.Binding(ClientSecret), ClientSecretValue),
        },
    ];

    public static void AddTo(IDictionary<string, ActivationBinding> bindings)
    {
        bindings[Issuer] = new(ActivationBinding.UrlKind, new Uri(IssuerUrl));
        bindings[ClientId] = new(ActivationBinding.SecretKind, null);
        bindings[ClientSecret] = new(ActivationBinding.SecretKind, null);
    }
}
