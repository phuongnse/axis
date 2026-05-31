namespace Axis.Identity.Application.Services;

public interface IExternalAuthProviderRegistry
{
    IReadOnlyList<string> GetEnabledProviders();

    bool IsProviderEnabled(string providerName);
}
