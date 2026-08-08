using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Axis.Identity.Application.Services;

public sealed record ServiceIdentityPublicJwk(string Kid, string Thumbprint, string X, string Y);
public static class ServiceIdentityPublicJwkParser
{
    public static bool TryParse(string? value, out ServiceIdentityPublicJwk? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(value);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("d", out _) || root.TryGetProperty("k", out _)) return false;
            if (!TryString(root, "kty", out string kty) || kty != "EC" || !TryString(root, "crv", out string crv) || crv != "P-256" || !TryString(root, "kid", out string kid) || !TryString(root, "x", out string x) || !TryString(root, "y", out string y)) return false;
            if (kid.Length is < 1 or > 128 || !Base64Url.TryDecode(x, out byte[] xb) || !Base64Url.TryDecode(y, out byte[] yb) || xb.Length != 32 || yb.Length != 32) return false;
            using ECDsa ecdsa = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = xb, Y = yb } });
            string canonical = $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
            key = new ServiceIdentityPublicJwk(kid, Base64Url.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))), x, y);
            return true;
        }
        catch (CryptographicException) { return false; }
        catch (JsonException) { return false; }
    }
    private static bool TryString(JsonElement root, string name, out string value) { value = string.Empty; return root.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String && (value = v.GetString() ?? string.Empty).Length > 0; }
}
public static class Base64Url
{
    public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static bool TryDecode(string value, out byte[] bytes) { bytes = []; if (value.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_'))) return false; try { bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4)); return true; } catch (FormatException) { return false; } }
}
