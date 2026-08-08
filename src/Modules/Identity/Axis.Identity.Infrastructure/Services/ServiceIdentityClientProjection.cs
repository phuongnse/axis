using System.Text.Json;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Identity.Infrastructure.Services;

/// <summary>Stages the OpenIddict row in the same IdentityDbContext transaction as lifecycle state.</summary>
internal sealed class ServiceIdentityClientProjection(IdentityDbContext context) : IServiceIdentityClientProjection
{
    private const string OwnerProperty = "axis:service_identity_id";

    public async Task StageAsync(ServiceIdentity identity, CancellationToken ct = default)
    {
        DbSet<OpenIddictEntityFrameworkCoreApplication> applications = context.Set<OpenIddictEntityFrameworkCoreApplication>();
        OpenIddictEntityFrameworkCoreApplication? app = await applications.SingleOrDefaultAsync(x => x.ClientId == identity.ClientId, ct);
        if (app is null)
        {
            app = new OpenIddictEntityFrameworkCoreApplication
            {
                Id = Guid.NewGuid().ToString(),
                ClientId = identity.ClientId,
                ConcurrencyToken = Guid.NewGuid().ToString(),
            };
            await applications.AddAsync(app, ct);
        }
        else if (!IsOwnedBy(app.Properties, identity.Id))
        {
            throw new ServiceIdentityClientProjectionException(
                "The OAuth client identifier belongs to another application.");
        }
        else
        {
            app.ConcurrencyToken = Guid.NewGuid().ToString();
        }

        app.ClientType = ClientTypes.Confidential;
        app.ConsentType = ConsentTypes.Systematic;
        app.DisplayName = identity.ClientId;
        app.Permissions = JsonSerializer.Serialize(new[]
        {
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.ClientCredentials,
        });
        app.Requirements = "[]";
        app.Properties = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [OwnerProperty] = identity.Id.ToString(),
        });
        app.JsonWebKeySet = JsonSerializer.Serialize(new
        {
            keys = identity.Keys
            .Where(key => key.Status == ServiceIdentityKeyStatus.Active
                && identity.Status == ServiceIdentityStatus.Active
                && identity.WorkspaceGrantStatus == ServiceWorkspaceGrantStatus.Active)
            .Select(key => new
            {
                kty = "EC",
                crv = "P-256",
                kid = key.Kid,
                use = "sig",
                alg = "ES256",
                x = key.X,
                y = key.Y,
            })
        });
    }

    private static bool IsOwnedBy(string? properties, Guid serviceIdentityId)
    {
        if (string.IsNullOrWhiteSpace(properties))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(properties);
            return document.RootElement.TryGetProperty(OwnerProperty, out JsonElement owner)
                && owner.ValueKind == JsonValueKind.String
                && Guid.TryParse(owner.GetString(), out Guid value)
                && value == serviceIdentityId;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
