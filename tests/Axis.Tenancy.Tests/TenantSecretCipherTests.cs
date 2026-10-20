using System.Text;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy.Tests;

public sealed class TenantSecretCipherTests
{
    private const string PlainText = "p4ssw0rd-9d2f-Ünï";

    private static readonly byte[] _key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private readonly TenantSecretCipher _cipher = CipherFor(_key);

    [Fact]
    public void An_encrypted_value_is_28_bytes_longer_hides_the_plain_text_and_decrypts_back()
    {
        var plain = Encoding.UTF8.GetBytes(PlainText);

        var stored = _cipher.Encrypt("a", TenantSecretFields.RuntimePassword, PlainText);

        Assert.Equal(plain.Length + TenantSecretCipher.NonceSize + TenantSecretCipher.TagSize, stored.Length);
        Assert.Equal(28, stored.Length - plain.Length);
        Assert.False(Contains(stored, plain));
        Assert.Equal(PlainText, _cipher.Decrypt("a", TenantSecretFields.RuntimePassword, stored).Reveal());
    }

    [Fact]
    public void An_empty_text_round_trips()
    {
        var stored = _cipher.Encrypt("a", TenantSecretFields.SchemaPassword, "");

        Assert.Equal(28, stored.Length);
        Assert.Equal("", _cipher.Decrypt("a", TenantSecretFields.SchemaPassword, stored).Reveal());
    }

    [Fact]
    public void Two_encryptions_of_the_same_text_differ()
    {
        var first = _cipher.Encrypt("a", TenantSecretFields.RuntimePassword, PlainText);
        var second = _cipher.Encrypt("a", TenantSecretFields.RuntimePassword, PlainText);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Another_tenant_does_not_decrypt_the_value_and_the_error_names_the_tenant_and_the_field()
    {
        var stored = _cipher.Encrypt("a", TenantSecretFields.OidcClientSecret, PlainText);

        var exception = Assert.Throws<InvalidOperationException>(() => _cipher.Decrypt("b", TenantSecretFields.OidcClientSecret, stored));

        AssertNames(exception, "b", TenantSecretFields.OidcClientSecret);
    }

    [Fact]
    public void Another_field_does_not_decrypt_the_value_and_the_error_names_the_tenant_and_the_field()
    {
        var stored = _cipher.Encrypt("a", TenantSecretFields.RuntimePassword, PlainText);

        var exception = Assert.Throws<InvalidOperationException>(() => _cipher.Decrypt("a", TenantSecretFields.SchemaPassword, stored));

        AssertNames(exception, "a", TenantSecretFields.SchemaPassword);
    }

    [Fact]
    public void The_separator_keeps_tenant_ab_with_field_c_apart_from_tenant_a_with_field_bc()
    {
        var stored = _cipher.Encrypt("ab", "c", PlainText);

        Assert.Throws<InvalidOperationException>(() => _cipher.Decrypt("a", "bc", stored));
    }

    [Fact]
    public void A_changed_byte_does_not_decrypt()
    {
        var stored = _cipher.Encrypt("a", TenantSecretFields.RuntimePassword, PlainText);
        stored[TenantSecretCipher.NonceSize] ^= 1;

        var exception = Assert.Throws<InvalidOperationException>(() => _cipher.Decrypt("a", TenantSecretFields.RuntimePassword, stored));

        AssertNames(exception, "a", TenantSecretFields.RuntimePassword);
    }

    [Fact]
    public void Another_key_does_not_decrypt()
    {
        var stored = _cipher.Encrypt("a", TenantSecretFields.RuntimePassword, PlainText);
        var other = CipherFor(new byte[32]);

        var exception = Assert.Throws<InvalidOperationException>(() => other.Decrypt("a", TenantSecretFields.RuntimePassword, stored));

        AssertNames(exception, "a", TenantSecretFields.RuntimePassword);
    }

    [Fact]
    public void A_value_shorter_than_a_nonce_and_a_tag_does_not_decrypt()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => _cipher.Decrypt("a", TenantSecretFields.RuntimePassword, new byte[27]));

        AssertNames(exception, "a", TenantSecretFields.RuntimePassword);
    }

    [Fact]
    public void A_data_protection_key_round_trips_and_hides_the_xml()
    {
        var id = Guid.NewGuid();
        var plain = Encoding.UTF8.GetBytes(PlainText);

        var stored = _cipher.EncryptDataProtectionKey(id, PlainText);

        Assert.Equal(plain.Length + TenantSecretCipher.NonceSize + TenantSecretCipher.TagSize, stored.Length);
        Assert.False(Contains(stored, plain));
        Assert.Equal(PlainText, _cipher.DecryptDataProtectionKey(id, stored).Reveal());
    }

    [Fact]
    public void A_data_protection_key_does_not_decrypt_for_another_key_id()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var stored = _cipher.EncryptDataProtectionKey(id, PlainText);

        var exception = Assert.Throws<InvalidOperationException>(() => _cipher.DecryptDataProtectionKey(other, stored));

        Assert.Contains($"'{other}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Platform:SecretKey", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(PlainText, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_data_protection_key_does_not_decrypt_as_a_tenant_field_and_a_tenant_field_does_not_decrypt_as_a_key()
    {
        var id = Guid.NewGuid();
        var key = _cipher.EncryptDataProtectionKey(id, PlainText);
        var secret = _cipher.Encrypt("a", TenantSecretFields.RuntimePassword, PlainText);

        Assert.Throws<InvalidOperationException>(() => _cipher.Decrypt("a", TenantSecretFields.DataProtectionKey(id), key));
        Assert.Throws<InvalidOperationException>(() => _cipher.DecryptDataProtectionKey(id, secret));
    }

    [Fact]
    public void A_data_protection_key_field_names_the_key_id()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        Assert.Equal("data_protection_key:0f8fad5b-d9cb-469f-a165-70867728950e", TenantSecretFields.DataProtectionKey(id));
    }

    [Fact]
    public void A_binding_field_is_lower_case()
    {
        Assert.Equal("binding:purchasingtoken", TenantSecretFields.Binding("PurchasingToken"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not base64!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void A_missing_blank_non_base64_or_wrongly_sized_key_is_refused_naming_the_setting(string? value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:SecretKey"] = value })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => TenantSecretCipher.Load(configuration));

        Assert.Contains("Platform:SecretKey", exception.Message, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(value))
        {
            Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
        }
    }

    private static TenantSecretCipher CipherFor(byte[] key) =>
        TenantSecretCipher.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:SecretKey"] = Convert.ToBase64String(key) })
            .Build());

    private static void AssertNames(InvalidOperationException exception, string tenantId, string field)
    {
        Assert.Contains($"'{tenantId}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(field, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(PlainText, exception.Message, StringComparison.Ordinal);
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;
}
