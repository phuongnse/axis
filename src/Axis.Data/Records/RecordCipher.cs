using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Naming;

namespace Axis.Data.Records;

/// <summary>
/// What a record write or read needs to store and show the sensitive fields of a tenant: the
/// <see cref="SensitiveValues"/> service and the tenant id, which every associated data holds. A write or read
/// without one never falls back to clear text: a sensitive value to write, or a stored one to read, throws.
/// </summary>
public sealed record RecordCipher(SensitiveValues Values, string TenantId)
{
    internal const string MissingMessage = "A sensitive value needs the tenant's data keys.";

    /// <summary>
    /// Returns the columns to write for <paramref name="values"/>: each value in the column of its field, with the value of
    /// each sensitive field replaced by its stored bytes, or <see langword="null"/> for a null value. A field with an exact
    /// lookup gets a second entry for its lookup column, which holds the lookup hash. The record or row <paramref name="rowId"/>
    /// of <paramref name="entity"/> is the scope. Without a cipher the values stay as they are, so a sensitive value to write is
    /// refused when it is bound.
    /// </summary>
    /// <exception cref="TenantDataKeyMissingException">The tenant has no data key, or no lookup key for an exact lookup field.</exception>
    internal static async Task<IReadOnlyList<(string Column, RecordValue Value)>> SealAsync(
        RecordCipher? cipher, EntityModel entity, Guid rowId, IReadOnlyList<RecordValue> values, CancellationToken cancellationToken)
    {
        var scope = cipher is null ? default : new SensitiveValueScope(cipher.TenantId, entity.Id, rowId);
        var columns = new List<(string Column, RecordValue Value)>(values.Count);
        foreach (var value in values)
        {
            var column = EntityNaming.Column(value.Field.Name);
            if (cipher is null || !value.Field.Sensitive)
            {
                columns.Add((column, value));
            }
            else
            {
                columns.Add((column, value with { Value = await cipher.Values.EncryptAsync(scope, value, cancellationToken) }));
            }

            if (value.Field.ExactLookup)
            {
                columns.Add((
                    EntityNaming.LookupColumn(value.Field.Name),
                    cipher is null ? value : value with { Value = await cipher.Values.LookupAsync(scope, value, cancellationToken) }));
            }
        }

        return columns;
    }

    /// <summary>
    /// Returns <paramref name="record"/> with the stored bytes of each sensitive field decrypted. Throws when there are
    /// stored values and no cipher, so a value is never shown as bytes or dropped.
    /// </summary>
    /// <exception cref="SensitiveValueDecryptionException">A stored value does not decrypt.</exception>
    internal static async Task<Record> OpenAsync(
        RecordCipher? cipher,
        EntityModel entity,
        Record record,
        IReadOnlyList<(FieldModel Field, byte[] Stored)> storedValues,
        CancellationToken cancellationToken)
    {
        if (storedValues.Count == 0)
        {
            return record;
        }

        RequireCipher(cipher);
        var values = new Dictionary<string, JsonNode?>(record.Values, StringComparer.Ordinal);
        foreach (var (field, stored) in storedValues)
        {
            values[field.Name] = await DecryptAsync(cipher, entity, record.Id, field, stored, cancellationToken);
        }

        return record with { Values = values };
    }

    internal static Task<JsonNode> DecryptAsync(
        RecordCipher? cipher, EntityModel entity, Guid rowId, FieldModel field, byte[] stored, CancellationToken cancellationToken) =>
        DecryptAsync(cipher, entity.Id, rowId, field, stored, cancellationToken);

    internal static async Task<JsonNode> DecryptAsync(
        RecordCipher? cipher, Guid entityId, Guid rowId, FieldModel field, byte[] stored, CancellationToken cancellationToken)
    {
        RequireCipher(cipher);
        return await cipher.Values.DecryptAsync(new SensitiveValueScope(cipher.TenantId, entityId, rowId), field, stored, cancellationToken);
    }

    /// <summary>Throws when there is no cipher, for a read of a stored sensitive value or a write of one.</summary>
    internal static void RequireCipher([NotNull] RecordCipher? cipher)
    {
        if (cipher is null)
        {
            throw new InvalidOperationException(MissingMessage);
        }
    }
}
