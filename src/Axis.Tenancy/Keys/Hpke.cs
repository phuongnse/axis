using System.Security.Cryptography;
using System.Text;

namespace Axis.Tenancy.Keys;

/// <summary>
/// Hybrid public key encryption in base mode as RFC 9180 defines it, with the one suite of the cryptographic inventory in
/// <c>docs/reference/keys-and-data-protection.md#cryptographic-inventory</c>: DHKEM(P-256, HKDF-SHA256), HKDF-SHA256 and AES-256-GCM.
/// A value sealed with a public key opens only with the private key that belongs to it. The sealed value is <c>enc</c>, the 65 bytes of the
/// ephemeral public point (<c>0x04 || X || Y</c>), followed by the ciphertext and its 16-byte tag. It seals one value per ephemeral key, so the
/// nonce is the key schedule's base nonce. Any value that does not open, including one with a wrong length or a point that is not on
/// the curve, throws <see cref="CryptographicException"/>.
/// </summary>
public static class Hpke
{
    /// <summary>The KEM id of DHKEM(P-256, HKDF-SHA256).</summary>
    public const int KemId = 0x0010;

    /// <summary>The KDF id of HKDF-SHA256.</summary>
    public const int KdfId = 0x0001;

    /// <summary>The AEAD id of AES-256-GCM.</summary>
    public const int AeadId = 0x0002;

    /// <summary>The length of <c>enc</c>, an uncompressed P-256 point.</summary>
    public const int EncLength = 65;

    private const int CoordinateLength = 32;
    private const int TagLength = 16;
    private const int NonceLength = 12;
    private const int SecretLength = 32;
    private static readonly byte[] _version = Encoding.ASCII.GetBytes("HPKE-v1");

    /// <summary>Seals <paramref name="plaintext"/> so that only the holder of the private key of <paramref name="recipientPublic"/> opens it.</summary>
    public static byte[] Seal(ECParameters recipientPublic, ReadOnlySpan<byte> info, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> plaintext)
    {
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Seal(recipientPublic, ephemeral, AeadId, info, aad, plaintext);
    }

    /// <summary>Opens a value that <see cref="Seal(ECParameters, ReadOnlySpan{byte}, ReadOnlySpan{byte}, ReadOnlySpan{byte})"/> made with the public key of <paramref name="recipient"/>.</summary>
    public static byte[] Open(ECDiffieHellman recipient, ReadOnlySpan<byte> info, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> sealedValue) =>
        Open(recipient, AeadId, info, aad, sealedValue);

    internal static byte[] Seal(ECParameters recipientPublic, ECDiffieHellman ephemeral, int aeadId, ReadOnlySpan<byte> info, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> plaintext)
    {
        var recipientPoint = Point(recipientPublic);
        var (sharedSecret, enc) = Encap(ephemeral, recipientPoint);
        var (key, nonce) = KeySchedule(sharedSecret, info, aeadId);
        var result = new byte[EncLength + plaintext.Length + TagLength];
        enc.CopyTo(result, 0);
        using var aead = new AesGcm(key, TagLength);
        aead.Encrypt(nonce, plaintext, result.AsSpan(EncLength, plaintext.Length), result.AsSpan(EncLength + plaintext.Length, TagLength), aad);
        return result;
    }

    internal static byte[] Open(ECDiffieHellman recipient, int aeadId, ReadOnlySpan<byte> info, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> sealedValue)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        if (sealedValue.Length < EncLength + TagLength)
        {
            throw new CryptographicException("The sealed value is too short.");
        }

        try
        {
            var enc = sealedValue[..EncLength];
            var sharedSecret = Decap(recipient, enc);
            var (key, nonce) = KeySchedule(sharedSecret, info, aeadId);
            var ciphertext = sealedValue[EncLength..^TagLength];
            var plaintext = new byte[ciphertext.Length];
            using var aead = new AesGcm(key, TagLength);
            aead.Decrypt(nonce, ciphertext, sealedValue[^TagLength..], plaintext, aad);
            return plaintext;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException && exception.GetType() != typeof(CryptographicException))
        {
            throw new CryptographicException("The sealed value does not open.");
        }
    }

    /// <summary>DHKEM Encap: the shared secret and <c>enc</c>, from the ephemeral key and the recipient's uncompressed point.</summary>
    internal static (byte[] SharedSecret, byte[] Enc) Encap(ECDiffieHellman ephemeral, byte[] recipientPoint)
    {
        using var recipient = ImportPoint(recipientPoint);
        var enc = Point(ephemeral.ExportParameters(false));
        var dh = ephemeral.DeriveRawSecretAgreement(recipient.PublicKey);
        return (ExtractAndExpand(dh, enc, recipientPoint), enc);
    }

    private static byte[] Decap(ECDiffieHellman recipient, ReadOnlySpan<byte> enc)
    {
        using var ephemeral = ImportPoint(enc);
        var dh = recipient.DeriveRawSecretAgreement(ephemeral.PublicKey);
        return ExtractAndExpand(dh, enc, Point(recipient.ExportParameters(false)));
    }

    private static byte[] ExtractAndExpand(byte[] dh, ReadOnlySpan<byte> enc, ReadOnlySpan<byte> recipientPoint)
    {
        var suite = SuiteId("KEM", KemId);
        var kemContext = Concat(enc.ToArray(), recipientPoint.ToArray());
        var prk = LabeledExtract(suite, [], "eae_prk", dh);
        return LabeledExpand(suite, prk, "shared_secret", kemContext, SecretLength);
    }

    /// <summary>The key schedule of base mode, without a PSK: the AEAD key and the base nonce.</summary>
    internal static (byte[] Key, byte[] BaseNonce) KeySchedule(byte[] sharedSecret, ReadOnlySpan<byte> info, int aeadId)
    {
        var keyLength = aeadId switch
        {
            1 => 16,
            AeadId => 32,
            _ => throw new ArgumentOutOfRangeException(nameof(aeadId), aeadId, "Only AES-128-GCM (1) and AES-256-GCM (2) are known."),
        };
        var suite = Concat(Encoding.ASCII.GetBytes("HPKE"), I2Osp(KemId), I2Osp(KdfId), I2Osp(aeadId));
        var pskIdHash = LabeledExtract(suite, [], "psk_id_hash", []);
        var infoHash = LabeledExtract(suite, [], "info_hash", info.ToArray());
        var context = Concat([0x00], pskIdHash, infoHash);
        var secret = LabeledExtract(suite, sharedSecret, "secret", []);
        return (LabeledExpand(suite, secret, "key", context, keyLength), LabeledExpand(suite, secret, "base_nonce", context, NonceLength));
    }

    private static byte[] LabeledExtract(byte[] suite, byte[] salt, string label, byte[] ikm) =>
        HKDF.Extract(HashAlgorithmName.SHA256, Concat(_version, suite, Encoding.ASCII.GetBytes(label), ikm), salt);

    private static byte[] LabeledExpand(byte[] suite, byte[] prk, string label, byte[] info, int length) =>
        HKDF.Expand(HashAlgorithmName.SHA256, prk, length, Concat(I2Osp(length), _version, suite, Encoding.ASCII.GetBytes(label), info));

    private static byte[] SuiteId(string name, int id) => Concat(Encoding.ASCII.GetBytes(name), I2Osp(id));

    private static byte[] I2Osp(int value) => [(byte)(value >> 8), (byte)value];

    private static byte[] Point(ECParameters publicKey)
    {
        if (publicKey.Q.X is not { Length: CoordinateLength } x || publicKey.Q.Y is not { Length: CoordinateLength } y)
        {
            throw new CryptographicException("The public key is not an EC P-256 point.");
        }

        return Concat([0x04], x, y);
    }

    private static ECDiffieHellman ImportPoint(ReadOnlySpan<byte> point)
    {
        if (point.Length != EncLength || point[0] != 0x04)
        {
            throw new CryptographicException("The public key is not an uncompressed EC P-256 point.");
        }

        try
        {
            return ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = point[1..(1 + CoordinateLength)].ToArray(), Y = point[(1 + CoordinateLength)..].ToArray() },
            });
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            throw new CryptographicException("The public key is not an EC P-256 point.");
        }
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(part => part)];
}
