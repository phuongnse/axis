using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Axis.Tenancy.Keys;

namespace Axis.Tenancy.Tests.Keys;

public sealed class HpkeTests
{
    // RFC 9180 Appendix A.3.1: DHKEM(P-256, HKDF-SHA256), HKDF-SHA256, AES-128-GCM, base mode, sequence number 0.
    private const string InfoHex = "4f6465206f6e2061204772656369616e2055726e";
    private const string PkEmHex = "04a92719c6195d5085104f469a8b9814d5838ff72b60501e2c4466e5e67b325ac98536d7b61a1af4b78e5b7f951c0900be863c403ce65c9bfcb9382657222d18c4";
    private const string SkEmHex = "4995788ef4b9d6132b249ce59a77281493eb39af373d236a1fe415cb0c2d7beb";
    private const string PkRmHex = "04fe8c19ce0905191ebc298a9245792531f26f0cece2460639e8bc39cb7f706a826a779b4cf969b8a0e539c7f62fb3d30ad6aa8f80e30f1d128aafd68a2ce72ea0";
    private const string SkRmHex = "f3ce7fdae57e1a310d87f1ebbde6f328be0a99cdbcadf4d6589cf29de4b8ffd2";
    private const string SharedSecretHex = "c0d26aeab536609a572b07695d933b589dcf363ff9d93c93adea537aeabb8cb8";
    private const string KeyHex = "868c066ef58aae6dc589b6cfdd18f97e";
    private const string BaseNonceHex = "4e0bc5018beba4bf004cca59";
    private const string PlaintextHex = "4265617574792069732074727574682c20747275746820626561757479";
    private const string AadHex = "436f756e742d30";
    private const string CiphertextHex = "5ad590bb8baa577f8619db35a36311226a896e7342a6d836d8b7bcd2f20b6c7f9076ac232e3ab2523f39513434";

    private static readonly byte[] _info = Convert.FromHexString(InfoHex);
    private static readonly byte[] _aad = Convert.FromHexString(AadHex);
    private static readonly byte[] _plaintext = Convert.FromHexString(PlaintextHex);

    [Fact]
    public void The_key_schedule_and_the_first_ciphertext_match_the_rfc_9180_a_3_1_vector()
    {
        var recipientPublic = PublicOf(PkRmHex);
        using var ephemeral = PrivateOf(SkEmHex, PkEmHex);

        var (sharedSecret, enc) = Hpke.Encap(ephemeral, [.. Convert.FromHexString(PkRmHex)]);
        var (key, baseNonce) = Hpke.KeySchedule(sharedSecret, _info, aeadId: 1);
        var sealedValue = Hpke.Seal(recipientPublic, ephemeral, aeadId: 1, _info, _aad, _plaintext);

        Assert.Equal(SharedSecretHex, Convert.ToHexStringLower(sharedSecret));
        Assert.Equal(PkEmHex, Convert.ToHexStringLower(enc));
        Assert.Equal(KeyHex, Convert.ToHexStringLower(key));
        Assert.Equal(BaseNonceHex, Convert.ToHexStringLower(baseNonce));
        Assert.Equal(PkEmHex + CiphertextHex, Convert.ToHexStringLower(sealedValue));
    }

    [Fact]
    public void The_recipient_of_the_rfc_9180_vector_opens_its_first_ciphertext()
    {
        using var recipient = PrivateOf(SkRmHex, PkRmHex);

        var opened = Hpke.Open(recipient, aeadId: 1, _info, _aad, Convert.FromHexString(PkEmHex + CiphertextHex));

        Assert.Equal(_plaintext, opened);
    }

    [Fact]
    public void A_value_sealed_with_aes_256_gcm_opens_with_the_private_key_and_hides_the_plaintext()
    {
        using var recipient = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var plaintext = "sk-person-1234"u8.ToArray();

        var sealedValue = Hpke.Seal(recipient.ExportParameters(false), _info, _aad, plaintext);

        Assert.Equal(Hpke.EncLength + plaintext.Length + 16, sealedValue.Length);
        Assert.Equal(0x04, sealedValue[0]);
        Assert.False(sealedValue.AsSpan().IndexOf(plaintext) >= 0);
        Assert.Equal(plaintext, Hpke.Open(recipient, _info, _aad, sealedValue));
        Assert.NotEqual(sealedValue, Hpke.Seal(recipient.ExportParameters(false), _info, _aad, plaintext));
    }

    [Fact]
    public void A_value_that_was_changed_does_not_open()
    {
        using var recipient = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var sealedValue = Hpke.Seal(recipient.ExportParameters(false), _info, _aad, _plaintext);

        var changedCiphertext = (byte[])sealedValue.Clone();
        changedCiphertext[Hpke.EncLength] ^= 0x01;
        var changedTag = (byte[])sealedValue.Clone();
        changedTag[^1] ^= 0x01;
        var changedEnc = (byte[])sealedValue.Clone();
        changedEnc[10] ^= 0x01;

        Assert.Throws<CryptographicException>(() => Hpke.Open(recipient, _info, _aad, changedCiphertext));
        Assert.Throws<CryptographicException>(() => Hpke.Open(recipient, _info, _aad, changedTag));
        Assert.Throws<CryptographicException>(() => Hpke.Open(recipient, _info, _aad, changedEnc));
        Assert.Throws<CryptographicException>(() => Hpke.Open(recipient, [.. _info, 0x00], _aad, sealedValue));
        Assert.Throws<CryptographicException>(() => Hpke.Open(recipient, _info, [.. _aad, 0x00], sealedValue));
    }

    [Fact]
    public void A_value_does_not_open_with_another_private_key()
    {
        using var recipient = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var sealedValue = Hpke.Seal(recipient.ExportParameters(false), _info, _aad, _plaintext);

        Assert.Throws<CryptographicException>(() => Hpke.Open(other, _info, _aad, sealedValue));
    }

    [Fact]
    public void A_value_with_a_wrong_length_or_a_point_off_the_curve_does_not_open()
    {
        using var recipient = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var sealedValue = Hpke.Seal(recipient.ExportParameters(false), _info, _aad, _plaintext);
        var offCurve = (byte[])sealedValue.Clone();
        offCurve[Hpke.EncLength - 1] ^= 0x01;
        var compressed = (byte[])sealedValue.Clone();
        compressed[0] = 0x02;

        Assert.Throws<CryptographicException>(() => Hpke.Open(recipient, _info, _aad, sealedValue.AsSpan(0, Hpke.EncLength + 15)));
        Assert.Throws<CryptographicException>(() => Hpke.Open(recipient, _info, _aad, []));
        Assert.Throws<CryptographicException>(() => Hpke.Open(recipient, _info, _aad, offCurve));
        Assert.Throws<CryptographicException>(() => Hpke.Open(recipient, _info, _aad, compressed));
    }

    [Fact]
    public void A_public_key_that_is_not_a_p_256_point_is_refused_when_sealing()
    {
        var notAPoint = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = new byte[32], Y = new byte[32] } };
        var shortPoint = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = new byte[31], Y = new byte[31] } };

        Assert.Throws<CryptographicException>(() => Hpke.Seal(notAPoint, _info, _aad, _plaintext));
        Assert.Throws<CryptographicException>(() => Hpke.Seal(shortPoint, _info, _aad, _plaintext));
    }

    [Fact]
    public void The_key_id_is_the_rfc_7638_thumbprint_of_the_public_key()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);

        var (keyId, x, y) = JwkThumbprint.Of(parameters);

        var input = Encoding.UTF8.GetBytes($$"""{"crv":"P-256","kty":"EC","x":"{{x}}","y":"{{y}}"}""");
        Assert.Equal(Base64Url.EncodeToString(SHA256.HashData(input)), keyId);
        Assert.Equal(Base64Url.EncodeToString(parameters.Q.X!), x);
        Assert.Equal(Base64Url.EncodeToString(parameters.Q.Y!), y);
    }

    private static ECParameters PublicOf(string pointHex)
    {
        var point = Convert.FromHexString(pointHex);
        return new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = point[1..33], Y = point[33..] } };
    }

    private static ECDiffieHellman PrivateOf(string privateHex, string pointHex)
    {
        var parameters = PublicOf(pointHex);
        parameters.D = Convert.FromHexString(privateHex);
        return ECDiffieHellman.Create(parameters);
    }
}
