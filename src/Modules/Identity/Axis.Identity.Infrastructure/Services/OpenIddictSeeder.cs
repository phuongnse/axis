using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Identity.Infrastructure.Services;

/// <summary>
/// Seeds the two built-in OAuth2 clients on startup:
/// axis_spa — Authorization Code + PKCE (for the React SPA)
/// axis_m2m — Client Credentials (for external system integrations)
/// Idempotent: skips creation if the client already exists.
/// </summary>
public sealed class OpenIddictSeeder(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = services.CreateScope();

        IOpenIddictApplicationManager appManager =
            scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        IOpenIddictScopeManager scopeManager =
            scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        await SeedScopesAsync(scopeManager, cancellationToken);
        await SeedSpaClientAsync(appManager, cancellationToken);
        await SeedM2MClientAsync(appManager, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedScopesAsync(
        IOpenIddictScopeManager scopeManager, CancellationToken ct)
    {
        if (await scopeManager.FindByNameAsync("permissions", ct) is null)
        {
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = "permissions",
                DisplayName = "Axis permissions",
                Resources = { "axis_api" },
            }, ct);
        }
    }

    private static async Task SeedSpaClientAsync(
        IOpenIddictApplicationManager appManager, CancellationToken ct)
    {
        if (await appManager.FindByClientIdAsync("axis_spa", ct) is not null)
            return;

        await appManager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = "axis_spa",
            // Public client — no client secret (PKCE provides security instead)
            ClientType = ClientTypes.Public,
            DisplayName = "Axis SPA",
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Logout,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + Scopes.OpenId,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + Scopes.OfflineAccess,
                Permissions.Prefixes.Scope + "permissions",
            },
            // Allowed redirect URIs — front-end dev server + placeholder for production
            RedirectUris =
            {
                new Uri("http://localhost:3000/callback"),
                new Uri("http://localhost:5173/callback"),
            },
            PostLogoutRedirectUris =
            {
                new Uri("http://localhost:3000"),
                new Uri("http://localhost:5173"),
            },
            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange,
            },
        }, ct);
    }

    private static async Task SeedM2MClientAsync(
        IOpenIddictApplicationManager appManager, CancellationToken ct)
    {
        if (await appManager.FindByClientIdAsync("axis_m2m", ct) is not null)
            return;

        await appManager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = "axis_m2m",
            // Confidential client — uses a client secret
            ClientSecret = "axis-m2m-secret-change-in-production",
            ClientType = ClientTypes.Confidential,
            DisplayName = "Axis M2M (External Integrations)",
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.Prefixes.Scope + "permissions",
            },
        }, ct);
    }
}
