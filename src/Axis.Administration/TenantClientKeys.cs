using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Axis.Administration;

/// <summary>
/// Reads the client key of a tenant for <c>private_key_jwt</c>. Only an EC P-256 private key is accepted, because the
/// <c>fapi2</c> profile signs the client assertion with ES256. The key id is the RFC 7638 JWK thumbprint of the public key.
/// </summary>
public static class TenantClientKeys
{
    private const string P256Oid = "1.2.840.10045.3.1.7";

    /// <summary>
    /// Reads a PEM. On success returns the PKCS#8 PEM to store, the key id and the public key as a SubjectPublicKeyInfo PEM.
    /// Returns <see langword="false"/> when the text is not an EC P-256 private key.
    /// </summary>
    public static bool TryRead(string pem, out string pkcs8Pem, out string keyId, out string publicKeyPem)
    {
        pkcs8Pem = keyId = publicKeyPem = "";
        if (string.IsNullOrWhiteSpace(pem))
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            var parameters = key.ExportParameters(true);
            if (parameters.Curve.Oid.Value != P256Oid || parameters.D is null || parameters.Q.X is null || parameters.Q.Y is null)
            {
                return false;
            }

            var thumbprintInput = $$"""{"crv":"P-256","kty":"EC","x":"{{Base64Url.EncodeToString(parameters.Q.X)}}","y":"{{Base64Url.EncodeToString(parameters.Q.Y)}}"}""";
            keyId = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(thumbprintInput)));
            pkcs8Pem = key.ExportPkcs8PrivateKeyPem();
            publicKeyPem = key.ExportSubjectPublicKeyInfoPem();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            pkcs8Pem = keyId = publicKeyPem = "";
            return false;
        }
    }
}
