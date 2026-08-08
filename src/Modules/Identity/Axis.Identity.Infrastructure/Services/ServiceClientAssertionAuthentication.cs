using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Infrastructure.Services;

/// <summary>
/// Host-composable ES256 <c>private_key_jwt</c> validation. Token emission remains the
/// OpenIddict host responsibility so no raw assertion or access token crosses this boundary.
/// </summary>
public sealed class ServiceClientAssertionAuthentication(
    IServiceIdentityRepository identities,
    IServiceAssertionReplayStore replays,
    TimeProvider clock) : IServiceClientAssertionAuthentication
{
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
    private static readonly Guid GenericAttemptId =
        Guid.Parse("5f226c49-288a-4f0c-89a1-8c0469e04f7f");

    public async Task<ServiceAssertionAuthenticationResult?> AuthenticateAsync(
        ServiceAssertionAuthenticationRequest request,
        CancellationToken ct = default)
    {
        ServiceIdentity? identity = await identities.GetByClientIdAsync(request.ClientId, ct);
        if (identity is null
            || identity.Status != ServiceIdentityStatus.Active
            || identity.WorkspaceGrantStatus != ServiceWorkspaceGrantStatus.Active)
        {
            await TryRecordAuditAsync(Event(identity, "assertion_denied"), ct);
            return null;
        }

        if (!TryParse(
                request.Assertion,
                out Header header,
                out Payload payload,
                out byte[] signingInput,
                out byte[] signature)
            || header.Alg != "ES256"
            || string.IsNullOrEmpty(header.Kid))
        {
            await TryRecordAuditAsync(Event(identity, "assertion_denied"), ct);
            return null;
        }

        ServiceIdentityKey? key = identity.Keys.SingleOrDefault(value =>
            value.Kid == header.Kid
            && value.Status == ServiceIdentityKeyStatus.Active);
        if (key is null || !Verify(key.X, key.Y, signingInput, signature))
        {
            await TryRecordAuditAsync(Event(identity, "assertion_denied"), ct);
            return null;
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (payload.Iss != request.ClientId
            || payload.Sub != request.ClientId
            || payload.Audiences.Count != 1
            || !StringComparer.Ordinal.Equals(payload.Audiences[0], request.TokenEndpointAudience)
            || string.IsNullOrWhiteSpace(payload.Jti)
            || payload.Iat is null
            || payload.Exp is null)
        {
            await TryRecordAuditAsync(Event(identity, "assertion_denied"), ct);
            return null;
        }

        DateTime iat;
        DateTime exp;
        DateTime? notBefore;
        try
        {
            iat = DateTimeOffset.FromUnixTimeSeconds(payload.Iat.Value).UtcDateTime;
            exp = DateTimeOffset.FromUnixTimeSeconds(payload.Exp.Value).UtcDateTime;
            notBefore = payload.Nbf is long value
                ? DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime
                : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            await TryRecordAuditAsync(Event(identity, "assertion_denied"), ct);
            return null;
        }

        if (exp <= iat
            || exp - iat > MaxLifetime
            || now - iat > MaxLifetime + ClockSkew
            || iat - now > ClockSkew
            || exp < now - ClockSkew
            || (notBefore is DateTime nbf
                && (nbf > now + ClockSkew || nbf < iat)))
        {
            await TryRecordAuditAsync(Event(identity, "assertion_denied"), ct);
            return null;
        }

        string digest = Base64Url.Encode(
            SHA256.HashData(
                Encoding.UTF8.GetBytes($"{identity.Id:N}:{payload.Jti}")));
        string correlationId = Guid.NewGuid().ToString("N");
        try
        {
            bool accepted = await replays.TryAcceptAsync(
                digest,
                exp.Add(ClockSkew),
                Event(identity, "authenticated", correlationId, authenticated: true),
                Event(identity, "replay_denied", correlationId),
                ct);
            return accepted
                ? new ServiceAssertionAuthenticationResult(
                    identity.Id,
                    identity.WorkspaceId,
                    key.Id,
                    now.Add(MaxLifetime))
                : null;
        }
        catch
        {
            await TryRecordAuditAsync(
                Event(identity, "dependency_failure", correlationId),
                ct);
            return null;
        }
    }

    public async Task<bool> HasActiveAuthorityAsync(
        Guid serviceIdentityId,
        Guid keyId,
        CancellationToken ct = default)
    {
        ServiceIdentity? identity = await identities.GetByIdAsync(serviceIdentityId, ct);
        if (identity?.HasActiveAuthority(keyId) == true)
            return true;

        await TryRecordAuditAsync(Event(identity, "token_rejected"), ct);
        return false;
    }

    private async Task TryRecordAuditAsync(AuditEventV1 auditEvent, CancellationToken ct)
    {
        try
        {
            await replays.RecordAuditAsync(auditEvent, ct);
        }
        catch
        {
            // Required audit failures remain fail-closed. This boundary never turns
            // an unavailable audit store into authentication authority.
        }
    }

    private AuditEventV1 Event(
        ServiceIdentity? identity,
        string outcome,
        string? correlationId = null,
        bool authenticated = false) =>
        new(
            Guid.NewGuid(),
            authenticated ? AuditActorKindV1.ServiceIdentity : AuditActorKindV1.Anonymous,
            authenticated ? identity!.Id : null,
            identity?.Id ?? GenericAttemptId,
            identity?.WorkspaceId,
            "identity.service_authentication",
            identity is null ? "authentication-attempt" : "service-identity",
            identity?.Id ?? GenericAttemptId,
            outcome,
            clock.GetUtcNow(),
            correlationId ?? Guid.NewGuid().ToString("N"));

    private static bool Verify(
        string x,
        string y,
        byte[] input,
        byte[] signature)
    {
        if (!Base64Url.TryDecode(x, out byte[] xb)
            || !Base64Url.TryDecode(y, out byte[] yb)
            || signature.Length != 64)
            return false;

        try
        {
            using ECDsa algorithm = ECDsa.Create(
                new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = xb, Y = yb },
                });
            return algorithm.VerifyData(
                input,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool TryParse(
        string jwt,
        out Header header,
        out Payload payload,
        out byte[] input,
        out byte[] signature)
    {
        header = new();
        payload = new();
        input = [];
        signature = [];
        string[] parts = jwt.Split('.');
        if (parts.Length != 3
            || !Base64Url.TryDecode(parts[0], out byte[] headerBytes)
            || !Base64Url.TryDecode(parts[1], out byte[] payloadBytes)
            || !Base64Url.TryDecode(parts[2], out signature))
            return false;

        try
        {
            using JsonDocument headerDocument = JsonDocument.Parse(headerBytes);
            using JsonDocument payloadDocument = JsonDocument.Parse(payloadBytes);
            header = new Header(
                String(headerDocument.RootElement, "alg"),
                String(headerDocument.RootElement, "kid"));
            payload = new Payload(
                String(payloadDocument.RootElement, "iss"),
                String(payloadDocument.RootElement, "sub"),
                Audiences(payloadDocument.RootElement),
                Long(payloadDocument.RootElement, "iat"),
                Long(payloadDocument.RootElement, "exp"),
                Long(payloadDocument.RootElement, "nbf"),
                String(payloadDocument.RootElement, "jti"));
            input = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? String(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static long? Long(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property)
        && property.TryGetInt64(out long result)
            ? result
            : null;

    private static IReadOnlyList<string> Audiences(JsonElement value) =>
        !value.TryGetProperty("aud", out JsonElement property)
            ? []
            : property.ValueKind == JsonValueKind.String
                ? [property.GetString()!]
                : property.ValueKind == JsonValueKind.Array
                    ? property.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!)
                        .ToArray()
                    : [];

    private sealed record Header(string? Alg = null, string? Kid = null);

    private sealed record Payload(
        string? Iss = null,
        string? Sub = null,
        IReadOnlyList<string>? Values = null,
        long? Iat = null,
        long? Exp = null,
        long? Nbf = null,
        string? Jti = null)
    {
        public IReadOnlyList<string> Audiences { get; init; } = Values ?? [];
    }
}
