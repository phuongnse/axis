using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Tenancy;

namespace Axis.Processes.Instances;

/// <summary>
/// Gets the access tokens of connectors with <c>oauth2</c> auth through the client credentials grant
/// and keeps each in memory for each tenant and connector. The request posts
/// <c>grant_type=client_credentials</c>, with <c>scope</c> and <c>audience</c> when the connector sets
/// them, and sends the client id and secret in HTTP Basic authentication, both form-encoded as RFC
/// 6749 section 2.3.1 says. It goes through the connector client, so it follows no redirect, and it is
/// cancelled after the connector's timeout. A token stays until a minute before its
/// <c>expires_in</c>, or until the worker drops it after the system answers <c>401</c>. A token without <c>expires_in</c>, or with 60 seconds or less, is used for the
/// one call and not kept. Each entry also holds a hash of the token URL, the client id, the client
/// secret, the scope and the audience, so a changed binding or file gets a new token at once. Two calls
/// that find the cache empty at the same moment may each request a token. A failed request gives only
/// the status of the token endpoint and a fixed text, never its body, an exception message, the
/// token or the client secret. The token is never stored, logged or traced.
/// </summary>
internal sealed class ConnectorTokenCache(ConnectorClient client, TimeProvider clock) : IConnectorTokens
{
    private static readonly TimeSpan _refreshMargin = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<(string Tenant, Guid Connector), Entry> _entries = new();

    public async Task<ConnectorTokenResult> GetAsync(
        string tenantId,
        ConnectorModel connector,
        Uri tokenUrl,
        SecretValue clientId,
        SecretValue clientSecret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(tokenUrl);
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(clientSecret);

        var oauth = connector.Auth?.OAuth2 ?? throw new ArgumentException("The connector has no oauth2 auth.", nameof(connector));
        var id = clientId.Reveal();
        var secret = clientSecret.Reveal();
        var fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', tokenUrl.AbsoluteUri, id, secret, oauth.Scope, oauth.Audience)));
        var key = (tenantId, connector.Id);
        if (_entries.TryGetValue(key, out var entry)
            && clock.GetUtcNow() < entry.RefreshAt
            && CryptographicOperations.FixedTimeEquals(entry.Fingerprint, fingerprint))
        {
            return new ConnectorTokenResult(entry.Token, null, null);
        }

        var result = await RequestAsync(connector, oauth, tokenUrl, id, secret, cancellationToken);
        if (result.Token is not null && result.ExpiresIn > _refreshMargin)
        {
            _entries[key] = new Entry(result.Token, clock.GetUtcNow() + result.ExpiresIn.Value - _refreshMargin, fingerprint);
        }
        else
        {
            _entries.TryRemove(key, out _);
        }

        return new ConnectorTokenResult(result.Token, result.Status, result.Error);
    }

    public void Drop(string tenantId, ConnectorModel connector, SecretValue token)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(token);

        // GetAsync gives the instance it caches, so the same reference means no call has cached a newer token.
        var key = (tenantId, connector.Id);
        if (_entries.TryGetValue(key, out var entry) && ReferenceEquals(entry.Token, token))
        {
            _entries.TryRemove(KeyValuePair.Create(key, entry));
        }
    }

    private async Task<Issued> RequestAsync(
        ConnectorModel connector, ConnectorOAuth2Model oauth, Uri tokenUrl, string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        var name = connector.Name;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(connector.Timeout);
        try
        {
            var form = new List<KeyValuePair<string, string>> { new("grant_type", "client_credentials") };
            if (oauth.Scope is { } scope)
            {
                form.Add(new("scope", scope));
            }

            if (oauth.Audience is { } audience)
            {
                form.Add(new("audience", audience));
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl) { Content = new FormUrlEncodedContent(form) };
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{WebUtility.UrlEncode(clientId)}:{WebUtility.UrlEncode(clientSecret)}"));
            request.Headers.TryAddWithoutValidation("Authorization", $"Basic {credentials}");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var response = await client.SendAsync(request, timeout.Token);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                return Failure(status, $"The token request of the connector '{name}' answered with the status {status}.");
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return Read(body, status, name);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(null, $"The token request of the connector '{name}' timed out after {connector.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The exception message can hold a value, so the error names only the exception type.
            return Failure(null, $"The token request of the connector '{name}' could not be sent: {exception.GetType().FullName}.");
        }
    }

    /// <summary>Reads <c>access_token</c> and <c>expires_in</c> from a <c>2xx</c> body.</summary>
    private static Issued Read(string body, int status, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("access_token", out var token)
                && token.ValueKind == JsonValueKind.String
                && token.GetString() is { Length: > 0 } text)
            {
                TimeSpan? expiresIn = root.TryGetProperty("expires_in", out var seconds)
                    && seconds.ValueKind == JsonValueKind.Number
                    && seconds.TryGetDouble(out var value)
                    && value is > 0 and < 10_000_000_000
                        ? TimeSpan.FromSeconds(value)
                        : null;
                return new Issued(new SecretValue(text), expiresIn, status, null);
            }
        }
        catch (JsonException)
        {
            // The body is not JSON, which the fixed text below says without quoting it.
        }

        return Failure(status, $"The token request of the connector '{name}' answered with a body that holds no access token.");
    }

    private static Issued Failure(int? status, string error) => new(null, null, status, error);

    private sealed record Entry(SecretValue Token, DateTimeOffset RefreshAt, byte[] Fingerprint);

    private sealed record Issued(SecretValue? Token, TimeSpan? ExpiresIn, int? Status, string? Error);
}
