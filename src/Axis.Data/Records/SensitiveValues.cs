using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Tenancy.Keys;

namespace Axis.Data.Records;

/// <summary>Where a sensitive value lives: the tenant, the entity and the record. The field comes with the call.</summary>
public readonly record struct SensitiveValueScope(string TenantId, Guid EntityId, Guid RecordId);

/// <summary>
/// Encrypts and decrypts the value of a sensitive field with AES-256-GCM under the data keys of its tenant. The plain text
/// is the value as the record API shows it. A stored value is the number of the data key (4 bytes, big-endian), a 12-byte
/// nonce, the ciphertext and a 16-byte tag. The associated data is the tenant id, the entity id, the record id and the
/// lower-cased field name, each as UTF-8 bytes followed by one zero byte, so a value moved to another tenant, entity,
/// record or field does not decrypt. No message holds a value or a key.
/// <para>
/// An exact lookup field also keeps a lookup hash beside its encrypted value: the number of the newest lookup key (4 bytes,
/// big-endian) and the HMAC-SHA256 of the plain text under that key. The message starts with the tenant id, the entity id
/// and the lower-cased field name, each followed by one zero byte, which is the associated data without the record id.
/// Equal values of one field then give equal hashes in every record, and equal values of two fields give different ones.
/// </para>
/// </summary>
public sealed class SensitiveValues(TenantKeys keys)
{
    /// <summary>The size of the key number at the start of a stored value.</summary>
    public const int NumberSize = 4;

    public const int NonceSize = 12;

    public const int TagSize = 16;

    /// <summary>The size of a lookup hash: the key number and the 32-byte HMAC-SHA256.</summary>
    public const int LookupSize = NumberSize + 32;

    /// <summary>
    /// Returns the stored bytes of the value under the newest data key of the tenant, or <see langword="null"/> for a null value.
    /// </summary>
    /// <exception cref="TenantDataKeyMissingException">The tenant has no data key.</exception>
    public async Task<byte[]?> EncryptAsync(SensitiveValueScope scope, RecordValue value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrEmpty(scope.TenantId);

        return value.Value is null ? null : await EncryptTextAsync(scope, value.Field.Name, ToText(value), cancellationToken);
    }

    /// <summary>
    /// Returns the stored bytes of <paramref name="text"/>, the value as the record API shows it, under the newest data
    /// key of the tenant, with <paramref name="fieldName"/> in the associated data.
    /// </summary>
    /// <exception cref="TenantDataKeyMissingException">The tenant has no data key.</exception>
    public async Task<byte[]> EncryptTextAsync(SensitiveValueScope scope, string fieldName, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(scope.TenantId);

        var key = await keys.NewestAsync(scope.TenantId, TenantKeyKind.Data, cancellationToken)
            ?? throw new TenantDataKeyMissingException(scope.TenantId);

        var plain = Encoding.UTF8.GetBytes(text);
        var stored = new byte[NumberSize + NonceSize + plain.Length + TagSize];
        BinaryPrimitives.WriteInt32BigEndian(stored, key.Number);
        var nonce = stored.AsSpan(NumberSize, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key.Reveal(), TagSize);
        aes.Encrypt(
            nonce,
            plain,
            stored.AsSpan(NumberSize + NonceSize, plain.Length),
            stored.AsSpan(NumberSize + NonceSize + plain.Length, TagSize),
            AssociatedData(scope, fieldName));
        return stored;
    }

    /// <summary>
    /// Returns the lookup hash of the value under the newest lookup key of the tenant, or <see langword="null"/> for a null value.
    /// </summary>
    /// <exception cref="TenantDataKeyMissingException">The tenant has no lookup key.</exception>
    public async Task<byte[]?> LookupAsync(SensitiveValueScope scope, RecordValue value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrEmpty(scope.TenantId);

        return value.Value is null ? null : await LookupTextAsync(scope.TenantId, scope.EntityId, value.Field.Name, ToText(value), cancellationToken);
    }

    /// <summary>
    /// Returns the lookup hash of <paramref name="text"/>, the value as the record API shows it, under the newest lookup key
    /// of the tenant, for the field <paramref name="fieldName"/> of the entity <paramref name="entityId"/>.
    /// </summary>
    /// <exception cref="TenantDataKeyMissingException">The tenant has no lookup key.</exception>
    public async Task<byte[]> LookupTextAsync(string tenantId, Guid entityId, string fieldName, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(text);

        var key = await keys.NewestAsync(tenantId, TenantKeyKind.Lookup, cancellationToken)
            ?? throw new TenantDataKeyMissingException(tenantId, TenantKeyKind.Lookup);

        var message = new List<byte>(Parts([tenantId, entityId.ToString("D"), fieldName.ToLowerInvariant()]));
        message.AddRange(Encoding.UTF8.GetBytes(text));

        var stored = new byte[LookupSize];
        BinaryPrimitives.WriteInt32BigEndian(stored, key.Number);
        HMACSHA256.HashData(key.Reveal(), [.. message], stored.AsSpan(NumberSize));
        return stored;
    }

    /// <summary>Returns the value as <c>RecordQueries.ReadValue</c> gives it for a plain column of the field.</summary>
    /// <exception cref="SensitiveValueDecryptionException">
    /// The value is too short, names a key the tenant does not hold, was encrypted for another tenant, entity, record or field,
    /// or was changed. The message names the tenant, the entity, the field and the key number.
    /// </exception>
    public async Task<JsonValue> DecryptAsync(SensitiveValueScope scope, FieldModel field, byte[] stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(field);

        return FromText(field, await DecryptTextAsync(scope, field.Name, stored, cancellationToken));
    }

    /// <summary>Returns the text form of the value, as <see cref="EncryptTextAsync"/> took it, stored with <paramref name="fieldName"/>.</summary>
    /// <exception cref="SensitiveValueDecryptionException">The value does not decrypt, as <see cref="DecryptAsync"/> says.</exception>
    public async Task<string> DecryptTextAsync(SensitiveValueScope scope, string fieldName, byte[] stored, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentException.ThrowIfNullOrEmpty(scope.TenantId);

        if (stored.Length < NumberSize + NonceSize + TagSize)
        {
            throw new SensitiveValueDecryptionException(Failure(scope, fieldName, "is too short"));
        }

        var number = BinaryPrimitives.ReadInt32BigEndian(stored);
        var key = number < 1 ? null : await keys.FindAsync(scope.TenantId, TenantKeyKind.Data, number, cancellationToken);
        if (key is null)
        {
            throw new SensitiveValueDecryptionException(Failure(scope, fieldName, $"names data key {number}, which the tenant does not hold"));
        }

        var cipherLength = stored.Length - NumberSize - NonceSize - TagSize;
        var plain = new byte[cipherLength];
        try
        {
            using var aes = new AesGcm(key.Reveal(), TagSize);
            aes.Decrypt(
                stored.AsSpan(NumberSize, NonceSize),
                stored.AsSpan(NumberSize + NonceSize, cipherLength),
                stored.AsSpan(NumberSize + NonceSize + cipherLength, TagSize),
                plain,
                AssociatedData(scope, fieldName));
        }
        catch (CryptographicException exception)
        {
            throw new SensitiveValueDecryptionException(Failure(scope, fieldName, $"does not decrypt with data key {number}"), exception);
        }

        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>
    /// Returns the stored value decrypted with the associated data of <paramref name="fromField"/> and encrypted again under
    /// the newest data key with the associated data of <paramref name="toField"/>.
    /// </summary>
    /// <exception cref="SensitiveValueDecryptionException">The value does not decrypt.</exception>
    /// <exception cref="TenantDataKeyMissingException">The tenant has no data key.</exception>
    public async Task<byte[]> ReEncryptAsync(SensitiveValueScope scope, string fromField, string toField, byte[] stored, CancellationToken cancellationToken) =>
        await EncryptTextAsync(scope, toField, await DecryptTextAsync(scope, fromField, stored, cancellationToken), cancellationToken);

    private static string Failure(SensitiveValueScope scope, string fieldName, string reason) =>
        $"The stored value of field '{fieldName}' of entity '{scope.EntityId}' of tenant '{scope.TenantId}' {reason}.";

    // The text form is the record API value. A file field shows its file id.
    private static string ToText(RecordValue value)
    {
        if (value.Field.Type == FieldType.File)
        {
            return ((Guid)value.Value!).ToString("D");
        }

        var node = RecordValueJson.ToJson(value)!;
        return node is JsonValue json && json.TryGetValue<string>(out var text) ? text : node.ToJsonString();
    }

    // Mirrors RecordQueries.ReadValue.
    private static JsonValue FromText(FieldModel field, string text) =>
        field.Type switch
        {
            FieldType.Text or FieldType.Enum or FieldType.Date or FieldType.DateTime or FieldType.Reference or FieldType.File => JsonValue.Create(text),
            FieldType.Integer or FieldType.Decimal or FieldType.Money or FieldType.Boolean => (JsonValue)JsonNode.Parse(text)!,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field.Type, "A field of this type cannot be sensitive."),
        };

    private static byte[] AssociatedData(SensitiveValueScope scope, string fieldName) =>
        Parts([scope.TenantId, scope.EntityId.ToString("D"), scope.RecordId.ToString("D"), fieldName.ToLowerInvariant()]);

    // Each part as UTF-8 bytes followed by one zero byte. The associated data and the start of a lookup message share it.
    private static byte[] Parts(string[] parts)
    {
        var data = new List<byte>();
        foreach (var part in parts)
        {
            data.AddRange(Encoding.UTF8.GetBytes(part));
            data.Add(0);
        }

        return [.. data];
    }
}

/// <summary>A tenant has no data key, or no lookup key, so a sensitive value cannot be written. It is never written in clear.</summary>
public sealed class TenantDataKeyMissingException(string tenantId, TenantKeyKind kind = TenantKeyKind.Data)
    : InvalidOperationException($"Tenant '{tenantId}' has no {(kind == TenantKeyKind.Lookup ? "lookup" : "data")} key, so a sensitive value cannot be written.")
{
    public string TenantId { get; } = tenantId;

    /// <summary>The kind of the key the tenant lacks.</summary>
    public TenantKeyKind Kind { get; } = kind;
}

/// <summary>A stored sensitive value does not decrypt. The message never holds the value or a key.</summary>
public sealed class SensitiveValueDecryptionException(string message, Exception? inner = null) : InvalidOperationException(message, inner);
