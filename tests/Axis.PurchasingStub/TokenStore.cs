using System.Security.Cryptography;

namespace Axis.PurchasingStub;

/// <summary>
/// The access tokens the stub has issued, with the client id each was issued to and when it expires.
/// A token stays known after it expires, so that the stub refuses it by its time and not by chance.
/// </summary>
internal sealed class TokenStore
{
    private const string Scheme = "Bearer ";

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (string ClientId, DateTimeOffset ExpiresAt)> _tokens = new(StringComparer.Ordinal);

    /// <summary>Issues a token to the client that lasts the lifetime and returns it.</summary>
    public string Issue(string clientId, TimeSpan lifetime)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        lock (_lock)
        {
            _tokens.Add(token, (clientId, DateTimeOffset.UtcNow + lifetime));
        }

        return token;
    }

    /// <summary>
    /// Returns the client id the <c>Authorization</c> header's bearer token was issued to, or
    /// <see langword="null"/> when the header holds no token, an unknown one or an expired one.
    /// </summary>
    public string? Find(string authorization)
    {
        if (!authorization.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorization[Scheme.Length..].Trim();
        lock (_lock)
        {
            return _tokens.TryGetValue(token, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow ? entry.ClientId : null;
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
