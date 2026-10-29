using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Axis.Integration.Tests;

/// <summary>
/// Sends the server's calls to Keycloak and records their paths. It can change the provider metadata, and it can change
/// the claims of the ID token that the token endpoint returns. The changed token is signed with a key of its own, which
/// the handler adds to the key set that Keycloak publishes.
/// </summary>
internal sealed class IdTokenRewriteHandler(Action<JsonObject>? editMetadata = null, Action<JsonObject>? editIdToken = null)
    : DelegatingHandler(new HttpClientHandler())
{
    private const string KeyId = "axis-test";

    private readonly ConcurrentQueue<string> _paths = new();
    private readonly RSA _rsa = RSA.Create(2048);

    public IReadOnlyCollection<string> Paths => _paths;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        _paths.Enqueue(path);
        var response = await base.SendAsync(request, cancellationToken);
        if (editMetadata is not null && path.EndsWith(".well-known/openid-configuration", StringComparison.Ordinal))
        {
            await RewriteAsync(response, editMetadata, cancellationToken);
        }
        else if (editIdToken is not null && path.EndsWith("/protocol/openid-connect/certs", StringComparison.Ordinal))
        {
            await RewriteAsync(response, AddKey, cancellationToken);
        }
        else if (editIdToken is not null && path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
        {
            await RewriteAsync(response, Resign, cancellationToken);
        }

        return response;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rsa.Dispose();
        }

        base.Dispose(disposing);
    }

    private static async Task RewriteAsync(HttpResponseMessage response, Action<JsonObject> edit, CancellationToken cancellationToken)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
        edit(body);
        response.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
    }

    private void AddKey(JsonObject keySet)
    {
        var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa) { KeyId = KeyId });
        keySet["keys"]!.AsArray().Add(new JsonObject
        {
            ["kty"] = "RSA",
            ["use"] = "sig",
            ["alg"] = SecurityAlgorithms.RsaSha256,
            ["kid"] = KeyId,
            ["n"] = key.N,
            ["e"] = key.E,
        });
    }

    private void Resign(JsonObject tokenResponse)
    {
        var idToken = tokenResponse["id_token"]!.GetValue<string>();
        var claims = JsonNode.Parse(Base64UrlEncoder.Decode(idToken.Split('.')[1]))!.AsObject();
        editIdToken!(claims);
        var credentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256);
        tokenResponse["id_token"] = new JsonWebTokenHandler().CreateToken(claims.ToJsonString(), credentials);
    }
}
