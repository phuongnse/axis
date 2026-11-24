using System.Text;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy.Keys;

/// <summary>
/// Wraps and unwraps the keys that a host holds only in wrapped form: the installation key, <c>Platform:SecretKey</c>, and the agent host's
/// private key, <c>Ai:HostKeyFile</c>. A wrapped key is bound to its purpose, so a key wrapped for one purpose does not unwrap for the other.
/// See <c>docs/reference/keys-and-data-protection.md#key-policy</c>.
/// </summary>
public interface IKeyService
{
    /// <summary>Returns the wrapped key in base64.</summary>
    string Wrap(string purpose, string key);

    /// <summary>
    /// Unwraps a key. The <paramref name="setting"/> only names the setting in a message.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The value is plain, is wrapped for another purpose or with another key, or was changed. The message names the setting and never a value.
    /// </exception>
    SecretValue Unwrap(string purpose, string setting, string wrapped);
}

/// <summary>Reads the key service of a host from <c>KeyService:Kind</c> and <c>KeyService:KeyFile</c>.</summary>
public static class KeyService
{
    /// <summary>The setting that turns the key service on. The only kind is <c>file</c>.</summary>
    public const string KindSetting = "KeyService:Kind";

    /// <summary>The setting that names the key file of the <c>file</c> kind, 32 random bytes in base64.</summary>
    public const string KeyFileSetting = "KeyService:KeyFile";

    /// <summary>The purpose of <c>Platform:SecretKey</c> and <c>Platform:PreviousSecretKey</c>.</summary>
    public const string InstallationKey = "installation-key";

    /// <summary>The purpose of the private key in <c>Ai:HostKeyFile</c>.</summary>
    public const string AgentHostKey = "agent-host-key";

    private const string FileKind = "file";

    /// <summary>
    /// Returns the key service, or <see langword="null"/> when <c>KeyService:Kind</c> is blank, and then a host reads plain keys. A message
    /// never contains a path or a key.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The kind is not <c>file</c>, or <c>KeyService:KeyFile</c> is missing, cannot be read or does not hold 32 bytes in base64.
    /// </exception>
    public static IKeyService? Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var kind = configuration[KindSetting];
        if (string.IsNullOrWhiteSpace(kind))
        {
            return null;
        }

        if (kind.Trim() != FileKind)
        {
            throw new InvalidOperationException($"The setting {KindSetting} must be {FileKind}.");
        }

        var path = configuration[KeyFileSetting];
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"The setting {KeyFileSetting} is required.");
        }

        var key = KeyFile.Read(path, out var problem, KeyFileSetting) ?? throw new InvalidOperationException(problem);
        return new FileKeyService(TenantSecretCipher.FromKey(key));
    }
}

/// <summary>The key service of the <c>file</c> kind: it wraps through <see cref="TenantSecretCipher"/> under the key of the key file.</summary>
internal sealed class FileKeyService(TenantSecretCipher cipher) : IKeyService
{
    public string Wrap(string purpose, string key) => Convert.ToBase64String(cipher.WrapKey(purpose, key));

    public SecretValue Unwrap(string purpose, string setting, string wrapped)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentException.ThrowIfNullOrEmpty(setting);
        ArgumentNullException.ThrowIfNull(wrapped);

        // A plain key, a value of another purpose or key file and a text that is not base64 all fail the same way, and the exception is not
        // kept: it could quote the value.
        var message = $"The setting {setting} does not unwrap with the key of {KeyService.KeyFileSetting}.";
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(wrapped.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(message);
        }

        try
        {
            return cipher.UnwrapKey(purpose, bytes, message);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(message);
        }
    }
}
