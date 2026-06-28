using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Identity.Infrastructure.Services;

/// <summary>
/// Seeds the SPA OAuth2 client on startup.
/// Idempotent: creates the built-in client and updates it so local
/// redirect URI changes are applied without wiping the development database.
/// </summary>
public sealed class OpenIddictSeeder(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = services.CreateScope();

        IOpenIddictApplicationManager appManager =
            scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        await SeedSpaClientAsync(appManager, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedSpaClientAsync(
        IOpenIddictApplicationManager appManager, CancellationToken ct)
    {
        object? application = await appManager.FindByClientIdAsync("axis_spa", ct);
        OpenIddictApplicationDescriptor descriptor = new()
        {
            ClientId = "axis_spa",
            // Public PKCE client; no client secret.
            ClientType = ClientTypes.Public,
            DisplayName = "Axis SPA",
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + Scopes.OpenId,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
            },
            // Local SPA dev ports.
            RedirectUris =
            {
                new Uri("https://localhost:3000/callback"),
                new Uri("https://localhost:5173/callback"),
                new Uri("https://web:3000/callback"),
            },
            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange,
            },
        };

        if (application is null)
            await appManager.CreateAsync(descriptor, ct);
        else
            await appManager.UpdateAsync(application, descriptor, ct);
    }
}
