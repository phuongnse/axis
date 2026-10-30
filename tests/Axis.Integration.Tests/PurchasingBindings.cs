using Axis.Data;
using Axis.PurchasingStub;
using Axis.Tenancy;
using Axis.Tenancy.Platform;

namespace Axis.Integration.Tests;

/// <summary>
/// The sign-in bindings of the purchase request sample's purchasing system connector, which are the token URL, the client id and
/// the client secret, as configuration, activation bindings or directory rows. <c>PurchasingSystemUrl</c> is not among them.
/// The client id and secret are the defaults of the purchasing system stub.
/// </summary>
internal static class PurchasingBindings
{
    public const string TokenUrl = "PurchasingSystemTokenUrl";

    public const string ClientId = "PurchasingSystemClientId";

    public const string ClientSecret = "PurchasingSystemClientSecret";

    public const string PlaceholderTokenUrl = "https://purchasing.example.test/oauth/token";

    /// <summary>The value of the <see cref="ClientId"/> binding, the client id the stub accepts.</summary>
    public const string ClientIdValue = PurchasingStubApp.DefaultClientId;

    /// <summary>The value of the <see cref="ClientSecret"/> binding, the client secret the stub accepts.</summary>
    public const string ClientSecretValue = PurchasingStubApp.DefaultClientSecret;

    public static IEnumerable<KeyValuePair<string, string?>> Settings(string tenant, string tokenUrl = PlaceholderTokenUrl) =>
    [
        new($"Tenants:{tenant}:Bindings:{TokenUrl}:Kind", DeploymentBindingKinds.Url),
        new($"Tenants:{tenant}:Bindings:{TokenUrl}:Value", tokenUrl),
        new($"Tenants:{tenant}:Bindings:{ClientId}:Kind", DeploymentBindingKinds.Secret),
        new($"Tenants:{tenant}:Bindings:{ClientId}:Value", ClientIdValue),
        new($"Tenants:{tenant}:Bindings:{ClientSecret}:Kind", DeploymentBindingKinds.Secret),
        new($"Tenants:{tenant}:Bindings:{ClientSecret}:Value", ClientSecretValue),
    ];

    public static IReadOnlyList<TenantBindingRow> Rows(string tenantId, string tokenUrl = PlaceholderTokenUrl) =>
    [
        new() { TenantId = tenantId, Name = TokenUrl, Kind = DeploymentBindingKinds.Url, Url = tokenUrl },
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
        bindings[TokenUrl] = new(ActivationBinding.UrlKind, new Uri(PlaceholderTokenUrl));
        bindings[ClientId] = new(ActivationBinding.SecretKind, null);
        bindings[ClientSecret] = new(ActivationBinding.SecretKind, null);
    }
}
