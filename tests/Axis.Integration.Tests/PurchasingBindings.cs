using Axis.Data;
using Axis.PurchasingStub;
using Axis.Tenancy;
using Axis.Tenancy.Platform;

namespace Axis.Integration.Tests;

/// <summary>
/// The sign-in bindings of the purchase request sample's purchasing system connector, which are the token URL, the client id and
/// the client secret, and the bindings of its approval webhook, which are the webhook URL and the webhook secret, as configuration,
/// activation bindings or directory rows. <c>PurchasingSystemUrl</c> is not among them.
/// The client id and secret and the webhook secret are the defaults of the purchasing system stub.
/// </summary>
internal static class PurchasingBindings
{
    public const string TokenUrl = "PurchasingSystemTokenUrl";

    public const string ClientId = "PurchasingSystemClientId";

    public const string ClientSecret = "PurchasingSystemClientSecret";

    public const string WebhookUrl = "PurchaseRequestApprovedWebhookUrl";

    public const string WebhookSecret = "PurchaseRequestApprovedWebhookSecret";

    public const string PlaceholderTokenUrl = "https://purchasing.example.test/oauth/token";

    public const string PlaceholderWebhookUrl = "https://purchasing.example.test/webhooks/purchase-request-approved";

    /// <summary>The value of the <see cref="ClientId"/> binding, the client id the stub accepts.</summary>
    public const string ClientIdValue = PurchasingStubApp.DefaultClientId;

    /// <summary>The value of the <see cref="ClientSecret"/> binding, the client secret the stub accepts.</summary>
    public const string ClientSecretValue = PurchasingStubApp.DefaultClientSecret;

    /// <summary>The value of the <see cref="WebhookSecret"/> binding, the secret the stub signs its webhook checks with.</summary>
    public const string WebhookSecretValue = PurchasingStubApp.DefaultWebhookSecret;

    public static IEnumerable<KeyValuePair<string, string?>> Settings(string tenant, string tokenUrl = PlaceholderTokenUrl, string webhookUrl = PlaceholderWebhookUrl) =>
    [
        new($"Tenants:{tenant}:Bindings:{TokenUrl}:Kind", DeploymentBindingKinds.Url),
        new($"Tenants:{tenant}:Bindings:{TokenUrl}:Value", tokenUrl),
        new($"Tenants:{tenant}:Bindings:{ClientId}:Kind", DeploymentBindingKinds.Secret),
        new($"Tenants:{tenant}:Bindings:{ClientId}:Value", ClientIdValue),
        new($"Tenants:{tenant}:Bindings:{ClientSecret}:Kind", DeploymentBindingKinds.Secret),
        new($"Tenants:{tenant}:Bindings:{ClientSecret}:Value", ClientSecretValue),
        new($"Tenants:{tenant}:Bindings:{WebhookUrl}:Kind", DeploymentBindingKinds.Url),
        new($"Tenants:{tenant}:Bindings:{WebhookUrl}:Value", webhookUrl),
        new($"Tenants:{tenant}:Bindings:{WebhookSecret}:Kind", DeploymentBindingKinds.Secret),
        new($"Tenants:{tenant}:Bindings:{WebhookSecret}:Value", WebhookSecretValue),
    ];

    public static IReadOnlyList<TenantBindingRow> Rows(string tenantId, string tokenUrl = PlaceholderTokenUrl, string webhookUrl = PlaceholderWebhookUrl) =>
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
        new() { TenantId = tenantId, Name = WebhookUrl, Kind = DeploymentBindingKinds.Url, Url = webhookUrl },
        new()
        {
            TenantId = tenantId,
            Name = WebhookSecret,
            Kind = DeploymentBindingKinds.Secret,
            SecretValue = AdminScenario.Cipher.Encrypt(tenantId, TenantSecretFields.Binding(WebhookSecret), WebhookSecretValue),
        },
    ];

    public static void AddTo(IDictionary<string, ActivationBinding> bindings)
    {
        bindings[TokenUrl] = new(ActivationBinding.UrlKind, new Uri(PlaceholderTokenUrl));
        bindings[ClientId] = new(ActivationBinding.SecretKind, null);
        bindings[ClientSecret] = new(ActivationBinding.SecretKind, null);
        bindings[WebhookUrl] = new(ActivationBinding.UrlKind, new Uri(PlaceholderWebhookUrl));
        bindings[WebhookSecret] = new(ActivationBinding.SecretKind, null);
    }
}
