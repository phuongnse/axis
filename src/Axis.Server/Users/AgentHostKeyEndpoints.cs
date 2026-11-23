using System.Buffers.Text;
using Axis.Server.Http;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;

namespace Axis.Server.Users;

/// <summary>
/// <c>GET /api/agent-host/key</c> answers the public key that the agent host published in <c>platform.host_keys</c> to a signed-in
/// person: <c>{ keyId, kem, kdf, aead, publicKey: { kty, crv, x, y } }</c>, with the suite ids of <see cref="Hpke"/> and the key as a JWK.
/// A person seals a value with it that only the agent host opens. The server holds no private half and does not reference the agent
/// host. A request is checked in order: signed-in user (401), a published key (404, a problem titled
/// <c>No agent host key is published.</c>). See <c>docs/reference/keys-and-data-protection.md#cryptographic-inventory</c>.
/// </summary>
internal static class AgentHostKeyEndpoints
{
    public static void MapAgentHostKeyEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/agent-host/key", GetKeyAsync);

    private static async Task<IResult> GetKeyAsync(HttpContext context, HostKeyStore hostKeys, CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(context) is null)
        {
            return Grants.SignInRequired();
        }

        if (await hostKeys.FindAsync(HostKeyStore.AgentHost, cancellationToken) is not var (keyId, publicKey) || publicKey.Length != Hpke.EncLength)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No agent host key is published.");
        }

        return Results.Ok(new AgentHostKeyResponse(
            keyId,
            Hpke.KemId,
            Hpke.KdfId,
            Hpke.AeadId,
            new AgentHostPublicKey("EC", "P-256", Base64Url.EncodeToString(publicKey.AsSpan(1, 32)), Base64Url.EncodeToString(publicKey.AsSpan(33, 32)))));
    }
}

/// <summary>The answer of <c>GET /api/agent-host/key</c>.</summary>
/// <param name="KeyId">The RFC 7638 thumbprint of the public key.</param>
/// <param name="Kem">The KEM id, 16 for DHKEM(P-256, HKDF-SHA256).</param>
/// <param name="Kdf">The KDF id, 1 for HKDF-SHA256.</param>
/// <param name="Aead">The AEAD id, 2 for AES-256-GCM.</param>
/// <param name="PublicKey">The key as a JWK.</param>
internal sealed record AgentHostKeyResponse(string KeyId, int Kem, int Kdf, int Aead, AgentHostPublicKey PublicKey);

/// <summary>An EC P-256 public key as a JWK: the coordinates are base64url.</summary>
internal sealed record AgentHostPublicKey(string Kty, string Crv, string X, string Y);
