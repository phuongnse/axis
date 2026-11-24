using System.Security.Cryptography;
using System.Text;
using Axis.Tenancy.Keys;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy.Platform;

/// <summary>
/// Encrypts and decrypts the secrets of the tenant directory with AES-256-GCM under the installation key
/// <c>Platform:SecretKey</c>. With a key service set, see <see cref="KeyService"/>, the two key settings hold wrapped values. A stored value is a 12-byte nonce, the ciphertext and a 16-byte tag. The associated data
/// is the UTF-8 bytes of the tenant id, a single zero byte and the UTF-8 bytes of the field name, so a value copied to
/// another tenant or field does not decrypt. During a key rotation the optional <c>Platform:PreviousSecretKey</c> decrypts
/// the values that the current key does not. It never encrypts.
/// </summary>
public sealed class TenantSecretCipher
{
    /// <summary>The setting that holds the installation key, 32 bytes in base64.</summary>
    public const string KeySetting = "Platform:SecretKey";

    /// <summary>The optional setting that holds the previous installation key, in the same form. Set it only during a rotation.</summary>
    public const string PreviousKeySetting = "Platform:PreviousSecretKey";

    public const int NonceSize = 12;

    public const int TagSize = 16;

    public const int KeySize = 32;

    private readonly byte[] _key;

    private readonly byte[]? _previousKey;

    private TenantSecretCipher(byte[] key, byte[]? previousKey)
    {
        _key = key;
        _previousKey = previousKey;
    }

    /// <summary>Whether <c>Platform:PreviousSecretKey</c> is set.</summary>
    public bool HasPreviousKey => _previousKey is not null;

    /// <summary>
    /// Reads <c>Platform:SecretKey</c> and the optional <c>Platform:PreviousSecretKey</c>. A blank previous key counts as not
    /// set. With <c>KeyService:Kind</c> set, each setting holds a wrapped key, which the key service unwraps before the checks below. A
    /// message never contains a value or a path.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>Platform:SecretKey</c> is missing, either setting is set and is not base64 or is not 32 bytes, the key service settings are
    /// wrong, or either setting does not unwrap with the key service.
    /// </exception>
    public static TenantSecretCipher Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return Load(configuration, KeyService.Load(configuration));
    }

    /// <summary>Loads the keys with a given key service, so a test can stand in for the one that <c>KeyService:Kind</c> picks.</summary>
    internal static TenantSecretCipher Load(IConfiguration configuration, IKeyService? keyService)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var key = ReadKey(configuration, KeySetting, keyService)!;
        var previousKey = ReadKey(configuration, PreviousKeySetting, keyService);
        return new TenantSecretCipher(key, previousKey);
    }

    /// <summary>
    /// A cipher that holds one key and no previous key, such as the cell's backup key, which wraps the backup keys of the tenants of a cell.
    /// </summary>
    /// <exception cref="ArgumentException">The key is not 32 bytes.</exception>
    public static TenantSecretCipher FromKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"The key must be {KeySize} bytes.", nameof(key));
        }

        return new TenantSecretCipher([.. key], null);
    }

    /// <summary>Returns the key of the setting, or <see langword="null"/> for a blank optional setting.</summary>
    private static byte[]? ReadKey(IConfiguration configuration, string setting, IKeyService? keyService)
    {
        var value = configuration[setting];
        if (string.IsNullOrWhiteSpace(value))
        {
            return setting == KeySetting ? throw new InvalidOperationException($"The setting {setting} is required.") : null;
        }

        if (keyService is not null)
        {
            value = keyService.Unwrap(KeyService.InstallationKey, setting, value).Reveal();
        }

        var buffer = new byte[64];
        if (!Convert.TryFromBase64String(value.Trim(), buffer, out var length))
        {
            throw new InvalidOperationException($"The setting {setting} is not base64.");
        }

        if (length != KeySize)
        {
            throw new InvalidOperationException($"The setting {setting} must be {KeySize} bytes in base64.");
        }

        return buffer[..KeySize];
    }

    /// <summary>Returns the nonce, the ciphertext and the tag of the plain text, bound to the tenant and the field.</summary>
    public byte[] Encrypt(string tenantId, string field, string plainText)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(field);
        ArgumentNullException.ThrowIfNull(plainText);

        return EncryptBound(tenantId, field, plainText);
    }

    /// <summary>
    /// Encrypts the XML of a data protection key. The associated data has an empty tenant id, so a key never
    /// decrypts as a tenant field.
    /// </summary>
    public byte[] EncryptDataProtectionKey(Guid id, string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        return EncryptBound("", TenantSecretFields.DataProtectionKey(id), xml);
    }

    /// <summary>Decrypts the stored XML of a data protection key.</summary>
    /// <exception cref="InvalidOperationException">The value does not decrypt for this key id and installation key.</exception>
    public SecretValue DecryptDataProtectionKey(Guid id, byte[] stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return DecryptBound("", TenantSecretFields.DataProtectionKey(id), stored, $"The data protection key '{id}' does not decrypt with {Keys()}.");
    }

    /// <summary>
    /// Wraps a key for a purpose, for the key service: the nonce, the ciphertext and the tag of the key's text. The associated data has an
    /// empty tenant id, so a wrapped key never decrypts as a tenant field, and the purpose, so it unwraps only for that purpose.
    /// </summary>
    public byte[] WrapKey(string purpose, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentNullException.ThrowIfNull(key);
        return EncryptBound("", TenantSecretFields.WrappedKey(purpose), key);
    }

    /// <summary>Unwraps a key that <see cref="WrapKey"/> wrapped for the purpose.</summary>
    /// <exception cref="InvalidOperationException">The value does not unwrap for this purpose and key. The message is <paramref name="failureMessage"/>.</exception>
    public SecretValue UnwrapKey(string purpose, byte[] wrapped, string failureMessage)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentNullException.ThrowIfNull(wrapped);
        ArgumentNullException.ThrowIfNull(failureMessage);
        return DecryptBound("", TenantSecretFields.WrappedKey(purpose), wrapped, failureMessage);
    }

    private byte[] EncryptBound(string tenantId, string field, string plainText)
    {
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

        return DecryptBound(tenantId, field, stored, $"The {field} of tenant '{tenantId}' does not decrypt with {Keys()}.");
    }

    // The settings that a failed decryption tried, for a message.
    private string Keys() => _previousKey is null ? KeySetting : $"{KeySetting} or {PreviousKeySetting}";

    private SecretValue DecryptBound(string tenantId, string field, byte[] stored, string failureMessage)
    {
        if (stored.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException(failureMessage);
        }

        var cipherLength = stored.Length - NonceSize - TagSize;
        var plain = new byte[cipherLength];
        var associatedData = AssociatedData(tenantId, field);
        try
        {
            Decrypt(_key, stored, cipherLength, plain, associatedData);
        }
        catch (CryptographicException exception)
        {
            if (_previousKey is null)
            {
                throw new InvalidOperationException(failureMessage, exception);
            }

            try
            {
                Decrypt(_previousKey, stored, cipherLength, plain, associatedData);
            }
            catch (CryptographicException)
            {
                throw new InvalidOperationException(failureMessage, exception);
            }
        }

        return new SecretValue(Encoding.UTF8.GetString(plain));
    }

    private static void Decrypt(byte[] key, byte[] stored, int cipherLength, byte[] plain, byte[] associatedData)
    {
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            stored.AsSpan(0, NonceSize),
            stored.AsSpan(NonceSize, cipherLength),
            stored.AsSpan(NonceSize + cipherLength, TagSize),
            plain,
            associatedData);
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
}
