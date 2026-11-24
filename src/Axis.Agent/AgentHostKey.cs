using System.Security.Cryptography;
using Axis.Tenancy.Keys;

namespace Axis.Agent;

/// <summary>
/// The key pair of the agent host, read from <c>Ai:HostKeyFile</c>, an EC P-256 private key as PEM. A value that is sealed with the public
/// half, see <see cref="Hpke"/>, opens only here: the server and the installation key hold no private half. The agent host publishes the
/// public half and the key id, the RFC 7638 thumbprint, and never the private half. See
/// <c>docs/reference/keys-and-data-protection.md#cryptographic-inventory</c>.
/// </summary>
public sealed class AgentHostKey : IDisposable
{
    private const string P256Oid = "1.2.840.10045.3.1.7";

    private readonly ECDiffieHellman _key;

    private AgentHostKey(ECDiffieHellman key, string keyId, byte[] publicKey)
    {
        _key = key;
        KeyId = keyId;
        PublicKey = publicKey;
    }

    /// <summary>The RFC 7638 thumbprint of the public key.</summary>
    public string KeyId { get; }

    /// <summary>The public key as the uncompressed point, 65 bytes: <c>0x04 || X || Y</c>.</summary>
    public byte[] PublicKey { get; }

    /// <summary>Reads the key file. The messages name the setting and never the path.</summary>
    /// <exception cref="InvalidOperationException">The file cannot be read or does not hold an EC P-256 private key.</exception>
    public static AgentHostKey Load(string file)
    {
        string pem;
        try
        {
            pem = File.ReadAllText(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The exception is not kept: its message holds the path.
            throw new InvalidOperationException($"'{AiSettings.HostKeyFileSetting}' names a file that cannot be read.");
        }

        var key = ECDiffieHellman.Create();
        try
        {
            key.ImportFromPem(pem);
            var parameters = key.ExportParameters(true);
            if (parameters.Curve.Oid.Value != P256Oid || parameters.D is null || parameters.Q.X is null || parameters.Q.Y is null)
            {
                throw new InvalidOperationException(NotP256);
            }

            var (keyId, _, _) = JwkThumbprint.Of(parameters);
            var publicKey = new byte[Hpke.EncLength];
            publicKey[0] = 0x04;
            parameters.Q.X.CopyTo(publicKey, 1);
            parameters.Q.Y.CopyTo(publicKey, 33);
            return new AgentHostKey(key, keyId, publicKey);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            // The exception is not kept: it can quote the key.
            key.Dispose();
            throw new InvalidOperationException(NotP256);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>Opens a value that <see cref="Hpke.Seal(ECParameters, ReadOnlySpan{byte}, ReadOnlySpan{byte}, ReadOnlySpan{byte})"/> sealed with <see cref="PublicKey"/>.</summary>
    /// <exception cref="CryptographicException">The value, <paramref name="info"/> or <paramref name="aad"/> is not the one it was sealed with.</exception>
    public byte[] Open(ReadOnlySpan<byte> info, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> sealedValue) => Hpke.Open(_key, info, aad, sealedValue);

    public void Dispose() => _key.Dispose();

    private static string NotP256 => $"'{AiSettings.HostKeyFileSetting}' must hold an EC P-256 private key.";
}
