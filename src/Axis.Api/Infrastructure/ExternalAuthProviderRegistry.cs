using Axis.Identity.Application.Services;
using Axis.Identity.Domain;
using Microsoft.Extensions.Configuration;

namespace Axis.Api.Infrastructure;

public sealed class ExternalAuthProviderRegistry(IConfiguration configuration) : IExternalAuthProviderRegistry
{
    private static readonly string[] SupportedProviders = ["microsoft", "google", "github"];

    public IReadOnlyList<string> GetEnabledProviders()
    {
        List<string> enabled = [];
        foreach (string provider in SupportedProviders)
        {
            if (IsProviderEnabled(provider))
                enabled.Add(provider);
        }

        return enabled;
    }

    public bool IsProviderEnabled(string providerName)
    {
        if (!TryParseProvider(providerName, out ExternalIdentityProvider _))
            return false;

        IConfigurationSection section = configuration.GetSection($"Authentication:ExternalProviders:{NormalizeConfigKey(providerName)}");
        string? clientId = section["ClientId"];
        return !string.IsNullOrWhiteSpace(clientId);
    }

    internal static bool TryParseProvider(string providerName, out ExternalIdentityProvider provider)
    {
        provider = default;
        if (string.IsNullOrWhiteSpace(providerName))
            return false;

        return providerName.Trim().ToLowerInvariant() switch
        {
            "microsoft" => Assign(ExternalIdentityProvider.Microsoft, out provider),
            "google" => Assign(ExternalIdentityProvider.Google, out provider),
            "github" => Assign(ExternalIdentityProvider.GitHub, out provider),
            _ => false,
        };
    }

    internal static string NormalizeConfigKey(string providerName) =>
        providerName.Trim().ToLowerInvariant() switch
        {
            "microsoft" => "Microsoft",
            "google" => "Google",
            "github" => "GitHub",
            _ => providerName,
        };

    private static bool Assign(ExternalIdentityProvider value, out ExternalIdentityProvider provider)
    {
        provider = value;
        return true;
    }
}
