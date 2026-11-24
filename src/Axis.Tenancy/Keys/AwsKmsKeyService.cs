using System.Text;
using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using Axis.Tenancy.Platform;

namespace Axis.Tenancy.Keys;

/// <summary>
/// The key service of the <c>aws-kms</c> kind. It wraps with KMS Encrypt and unwraps with KMS Decrypt at <c>KeyService:KeyId</c>, and sends the
/// purpose as the encryption context <c>axis:purpose</c>, so a key wrapped for one purpose does not unwrap for another. The host calls KMS
/// under its own AWS identity through the default credential chain, and the operator's key policy limits who may unwrap, see
/// <c>docs/reference/keys-and-data-protection.md#key-policy</c>. A wrapped value is the base64 of the KMS ciphertext of the key's text.
/// </summary>
internal sealed class AwsKmsKeyService(IAmazonKeyManagementService client, string keyId) : IKeyService
{
    private const string PurposeContext = "axis:purpose";

    /// <summary>
    /// Builds the service for a key id. The region comes from the key id when it is an ARN, and otherwise from the SDK's default region chain.
    /// </summary>
    /// <exception cref="InvalidOperationException">The key id is no ARN and the SDK finds no region.</exception>
    internal static AwsKmsKeyService Create(string keyId)
    {
        var config = new AmazonKeyManagementServiceConfig();
        if (Arn.TryParse(keyId, out var arn) && !string.IsNullOrEmpty(arn.Region))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(arn.Region);
        }

        try
        {
            return new AwsKmsKeyService(new AmazonKeyManagementServiceClient(config), keyId);
        }
        catch (AmazonClientException)
        {
            // The message of the SDK is left out: it is not ours to vouch that it holds no value.
            throw new InvalidOperationException($"The setting {KeyService.KeyIdSetting} needs a key ARN or an AWS region.");
        }
    }

    public string Wrap(string purpose, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentException.ThrowIfNullOrEmpty(key);

        var plaintext = Encoding.UTF8.GetBytes(key);
        try
        {
            using var stream = new MemoryStream(plaintext);
            var response = client.EncryptAsync(new EncryptRequest { KeyId = keyId, Plaintext = stream, EncryptionContext = ContextOf(purpose) }).GetAwaiter().GetResult();
            return Convert.ToBase64String(response.CiphertextBlob.ToArray());
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            throw new InvalidOperationException($"The key does not wrap at the key of {KeyService.KeyIdSetting} ({Describe(exception)}).");
        }
        finally
        {
            Array.Clear(plaintext);
        }
    }

    public SecretValue Unwrap(string purpose, string setting, string wrapped)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentException.ThrowIfNullOrEmpty(setting);
        ArgumentNullException.ThrowIfNull(wrapped);

        // The exceptions are not kept: the message of AWS quotes the role and the key, and a bad value could be quoted too.
        var message = $"The setting {setting} does not unwrap at the key of {KeyService.KeyIdSetting}";
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(wrapped.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{message}.");
        }

        byte[]? plaintext = null;
        try
        {
            using var stream = new MemoryStream(bytes);
            var response = client.DecryptAsync(new DecryptRequest { KeyId = keyId, CiphertextBlob = stream, EncryptionContext = ContextOf(purpose) }).GetAwaiter().GetResult();
            plaintext = response.Plaintext.ToArray();
            return new SecretValue(Encoding.UTF8.GetString(plaintext));
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            throw new InvalidOperationException($"{message} ({Describe(exception)}).");
        }
        finally
        {
            if (plaintext is not null)
            {
                Array.Clear(plaintext);
            }
        }
    }

    private static Dictionary<string, string> ContextOf(string purpose) => new() { [PurposeContext] = purpose };

    // The error code of KMS, such as AccessDeniedException, holds no value. Any other failure is a client that could not reach KMS.
    private static string Describe(Exception exception) =>
        exception is AmazonServiceException { ErrorCode: { Length: > 0 } code } ? code : "unreachable";
}
