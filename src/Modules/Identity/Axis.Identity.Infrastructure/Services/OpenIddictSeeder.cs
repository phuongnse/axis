using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Identity.Infrastructure.Services;

/// <summary>Reconciles deployment-owned OAuth/OIDC clients on every startup.</summary>
public sealed class OpenIddictSeeder(
    IServiceProvider services,
    OpenIddictClientCatalog catalog) : IHostedService
{
    internal const string ManagedOwnerProperty = "axis:client-catalog:owner";
    internal const string ManagedSchemaProperty = "axis:client-catalog:schema";
    internal const string ManagedProfileProperty = "axis:client-catalog:profile";
    internal const string ManagedOwner = "axis";

    private static readonly string[] NativePermissions =
    [
        Permissions.Endpoints.Authorization,
        Permissions.Endpoints.Token,
        Permissions.GrantTypes.AuthorizationCode,
        Permissions.ResponseTypes.Code,
        Permissions.Prefixes.Scope + Scopes.OpenId,
        Permissions.Scopes.Email,
        Permissions.Scopes.Profile,
    ];

    private static readonly string[] BffPermissions =
    [
        Permissions.Endpoints.Authorization,
        Permissions.Endpoints.EndSession,
        Permissions.Endpoints.PushedAuthorization,
        Permissions.Endpoints.Revocation,
        Permissions.Endpoints.Token,
        Permissions.GrantTypes.AuthorizationCode,
        Permissions.GrantTypes.RefreshToken,
        Permissions.ResponseTypes.Code,
        Permissions.Prefixes.Scope + Scopes.OpenId,
        Permissions.Prefixes.Scope + Scopes.OfflineAccess,
        Permissions.Scopes.Email,
        Permissions.Scopes.Profile,
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = services.CreateScope();
        IOpenIddictApplicationManager manager =
            scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        List<ExistingApplication> existing = [];
        await foreach (object application in manager.ListAsync(cancellationToken: cancellationToken))
        {
            string clientId = await manager.GetClientIdAsync(application, cancellationToken)
                ?? throw new InvalidOperationException("An OpenIddict application has no client ID.");
            existing.Add(new ExistingApplication(
                clientId,
                application,
                await IsManagedAsync(manager, application, cancellationToken)));
        }

        Dictionary<string, ExistingApplication> byClientId = existing.ToDictionary(
            application => application.ClientId,
            StringComparer.Ordinal);

        foreach (OpenIddictClientRegistration registration in catalog.Clients)
        {
            if (byClientId.TryGetValue(registration.ClientId, out ExistingApplication? current) &&
                !current.Managed)
            {
                throw new InvalidOperationException(
                    $"Configured client '{registration.ClientId}' collides with an application not owned by the client catalog.");
            }
        }

        foreach (OpenIddictClientRegistration registration in catalog.Clients)
        {
            OpenIddictApplicationDescriptor descriptor = CreateDescriptor(registration, catalog.SchemaVersion);
            if (byClientId.TryGetValue(registration.ClientId, out ExistingApplication? current))
                await manager.UpdateAsync(current.Application, descriptor, cancellationToken);
            else
                await manager.CreateAsync(descriptor, cancellationToken);
        }

        HashSet<string> configuredClientIds = catalog.Clients
            .Select(client => client.ClientId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (ExistingApplication application in existing)
        {
            if (application.Managed && !configuredClientIds.Contains(application.ClientId))
                await manager.DeleteAsync(application.Application, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static OpenIddictApplicationDescriptor CreateDescriptor(
        OpenIddictClientRegistration registration,
        int schemaVersion)
    {
        bool confidential = registration.Profile == OpenIddictClientProfile.WebBffConfidential;
        OpenIddictApplicationDescriptor descriptor = new()
        {
            ClientId = registration.ClientId,
            ClientSecret = confidential ? registration.ClientSecret : null,
            ClientType = confidential ? ClientTypes.Confidential : ClientTypes.Public,
            DisplayName = registration.DisplayName,
        };

        descriptor.Permissions.UnionWith(confidential ? BffPermissions : NativePermissions);
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        if (confidential)
            descriptor.Requirements.Add(Requirements.Features.PushedAuthorizationRequests);
        descriptor.RedirectUris.UnionWith(registration.RedirectUris);
        descriptor.PostLogoutRedirectUris.UnionWith(registration.PostLogoutRedirectUris);
        descriptor.Properties[ManagedOwnerProperty] = JsonSerializer.SerializeToElement(ManagedOwner);
        descriptor.Properties[ManagedSchemaProperty] = JsonSerializer.SerializeToElement(schemaVersion);
        descriptor.Properties[ManagedProfileProperty] = JsonSerializer.SerializeToElement(
            registration.Profile.ToString());
        return descriptor;
    }

    private static async ValueTask<bool> IsManagedAsync(
        IOpenIddictApplicationManager manager,
        object application,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, JsonElement> properties =
            await manager.GetPropertiesAsync(application, cancellationToken);
        return properties.TryGetValue(ManagedOwnerProperty, out JsonElement owner) &&
            owner.ValueKind == JsonValueKind.String &&
            string.Equals(owner.GetString(), ManagedOwner, StringComparison.Ordinal);
    }

    private sealed record ExistingApplication(string ClientId, object Application, bool Managed);
}
