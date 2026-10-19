using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy.Platform;

/// <summary>
/// Encrypts and decrypts the secrets of the tenant directory with AES-256-GCM under the installation key
/// <c>Platform:SecretKey</c>. A stored value is a 12-byte nonce, the ciphertext and a 16-byte tag. The associated data
/// is the UTF-8 bytes of the tenant id, a single zero byte and the UTF-8 bytes of the field name, so a value copied to
/// another tenant or field does not decrypt.
/// </summary>
public sealed class TenantSecretCipher
{
    /// <summary>The setting that holds the installation key, 32 bytes in base64.</summary>
    public const string KeySetting = "Platform:SecretKey";

    public const int NonceSize = 12;

    public const int TagSize = 16;

    public const int KeySize = 32;

    private readonly byte[] _key;

    private TenantSecretCipher(byte[] key)
    {
        _key = key;
    }

    /// <summary>Reads <c>Platform:SecretKey</c>. A message never contains the value.</summary>
    /// <exception cref="InvalidOperationException">The setting is missing, is not base64 or is not 32 bytes.</exception>
    public static TenantSecretCipher Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration[KeySetting];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The setting {KeySetting} is required when Tenancy:Source is 'directory'.");
        }

        var buffer = new byte[64];
        if (!Convert.TryFromBase64String(value.Trim(), buffer, out var length))
        {
            throw new InvalidOperationException($"The setting {KeySetting} is not base64.");
        }

        if (length != KeySize)
        {
            throw new InvalidOperationException($"The setting {KeySetting} must be {KeySize} bytes in base64.");
        }

        return new TenantSecretCipher(buffer[..KeySize]);
    }

    /// <summary>Returns the nonce, the ciphertext and the tag of the plain text, bound to the tenant and the field.</summary>
    public byte[] Encrypt(string tenantId, string field, string plainText)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(field);
        ArgumentNullException.ThrowIfNull(plainText);

        var plain = Encoding.UTF8.GetBytes(plainText);
        var stored = new byte[NonceSize + plain.Length + TagSize];
        var nonce = stored.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, stored.AsSpan(NonceSize, plain.Length), stored.AsSpan(NonceSize + plain.Length, TagSize), AssociatedData(tenantId, field));
        return stored;
    }

    /// <summary>Decrypts a stored value.</summary>
    /// <exception cref="InvalidOperationException">
    /// The value is too short, was encrypted for another tenant, field or key, or was changed. The message names the tenant and the field.
    /// </exception>
    public SecretValue Decrypt(string tenantId, string field, byte[] stored)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(field);
        ArgumentNullException.ThrowIfNull(stored);

        if (stored.Length < NonceSize + TagSize)
        {
            throw DecryptionFailed(tenantId, field, inner: null);
        }

        var cipherLength = stored.Length - NonceSize - TagSize;
        var plain = new byte[cipherLength];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(
                stored.AsSpan(0, NonceSize),
                stored.AsSpan(NonceSize, cipherLength),
                stored.AsSpan(NonceSize + cipherLength, TagSize),
                plain,
                AssociatedData(tenantId, field));
        }
        catch (CryptographicException exception)
        {
            throw DecryptionFailed(tenantId, field, exception);
        }

        return new SecretValue(Encoding.UTF8.GetString(plain));
    }

    private static byte[] AssociatedData(string tenantId, string field)
    {
        var tenant = Encoding.UTF8.GetBytes(tenantId);
        var name = Encoding.UTF8.GetBytes(field);
        var data = new byte[tenant.Length + 1 + name.Length];
        tenant.CopyTo(data, 0);
        name.CopyTo(data, tenant.Length + 1);
        return data;
    }

    private static InvalidOperationException DecryptionFailed(string tenantId, string field, Exception? inner) =>
        new($"The {field} of tenant '{tenantId}' does not decrypt with {KeySetting}.", inner);
}
