using System.Globalization;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Data.Schema;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Records;

/// <summary>
/// Runs the <see cref="SensitiveRewrite"/> of a plan command inside the activation transaction: it reads the ids and the
/// source column in batches by id, encrypts, decrypts, re-encrypts or hashes each value in C# with the tenant's data keys
/// and lookup keys, and stores the batch with the command's batch update. PostgreSQL never sees a key or a plain sensitive
/// value, and a value that does not decrypt throws, so the transaction rolls back. A rewrite without a cipher throws
/// before it reads a row, so a plain value is never written into an encrypted column.
/// </summary>
internal static class SensitiveRewriter
{
    private const int BatchSize = 1000;

    public static async Task RunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RecordCipher? cipher,
        SchemaCommand command,
        CancellationToken cancellationToken)
    {
        var rewrite = command.Rewrite ?? throw new ArgumentException("The command has no rewrite.", nameof(command));
        RecordCipher.RequireCipher(cipher);

        var last = Guid.Empty;
        while (true)
        {
            var batch = await ReadBatchAsync(connection, transaction, rewrite, last, cancellationToken);
            if (batch.Count == 0)
            {
                return;
            }

            last = batch[^1].Id;
            var ids = new Guid[batch.Count];
            var bytes = new byte[batch.Count][];
            var texts = new string[batch.Count];
            for (var index = 0; index < batch.Count; index++)
            {
                var (id, value) = batch[index];
                ids[index] = id;
                var scope = new SensitiveValueScope(cipher.TenantId, rewrite.EntityId, id);
                switch (rewrite.Kind)
                {
                    case SensitiveRewriteKind.Encrypt:
                        bytes[index] = (await cipher.Values.EncryptAsync(scope, new RecordValue(rewrite.Field!, value), cancellationToken))!;
                        break;
                    case SensitiveRewriteKind.Decrypt:
                        texts[index] = await cipher.Values.DecryptTextAsync(scope, rewrite.FieldName, (byte[])value!, cancellationToken);
                        break;
                    case SensitiveRewriteKind.Rename:
                        bytes[index] = await cipher.Values.ReEncryptAsync(scope, rewrite.FromFieldName!, rewrite.FieldName, (byte[])value!, cancellationToken);
                        break;
                    case SensitiveRewriteKind.Hash:
                        var text = await cipher.Values.DecryptTextAsync(scope, rewrite.FieldName, (byte[])value!, cancellationToken);
                        bytes[index] = await cipher.Values.LookupTextAsync(cipher.TenantId, rewrite.EntityId, rewrite.FieldName, text, cancellationToken);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown sensitive rewrite kind '{rewrite.Kind}'.");
                }
            }

            await using var update = new NpgsqlCommand(command.Sql, connection, transaction);
            update.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids });
            update.Parameters.Add(rewrite.Kind == SensitiveRewriteKind.Decrypt
                ? new NpgsqlParameter("values", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = texts }
                : new NpgsqlParameter("values", NpgsqlDbType.Array | NpgsqlDbType.Bytea) { Value = bytes });
            try
            {
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (PostgresException exception) when (rewrite.Kind == SensitiveRewriteKind.Decrypt)
            {
                // The message is fixed: PostgreSQL's own text can quote a decrypted value.
                throw new InvalidOperationException(
                    $"A decrypted value of field '{rewrite.FieldName}' of entity '{rewrite.EntityId}' does not fit the field's type ({exception.SqlState}).");
            }
        }
    }

    // The rows after last in id order that hold a value, each with the value as RecordValue documents it for the field.
    private static async Task<List<(Guid Id, object? Value)>> ReadBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SensitiveRewrite rewrite,
        Guid last,
        CancellationToken cancellationToken)
    {
        var id = EntityNaming.Quote(EntityNaming.IdColumn);
        var source = EntityNaming.Quote(rewrite.Source);
        var selected = rewrite.Kind == SensitiveRewriteKind.Encrypt && rewrite.Field!.Type is FieldType.Decimal or FieldType.Money
            ? $"{source}::text"
            : source;
        await using var read = new NpgsqlCommand(
            $"SELECT {id}, {selected} FROM {EntityNaming.QualifiedTable(rewrite.Table)} WHERE {id} > @last AND {source} IS NOT NULL ORDER BY {id} LIMIT {BatchSize.ToString(CultureInfo.InvariantCulture)}",
            connection,
            transaction);
        read.Parameters.AddWithValue("last", last);
        var batch = new List<(Guid, object?)>();
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            batch.Add((reader.GetGuid(0), rewrite.Kind == SensitiveRewriteKind.Encrypt ? ReadPlain(reader, rewrite.Field!) : reader.GetFieldValue<byte[]>(1)));
        }

        return batch;
    }

    private static object ReadPlain(NpgsqlDataReader reader, FieldModel field) =>
        field.Type switch
        {
            FieldType.Text or FieldType.Enum or FieldType.Decimal or FieldType.Money => reader.GetString(1),
            FieldType.Integer => reader.GetInt64(1),
            FieldType.Boolean => reader.GetBoolean(1),
            FieldType.Date => reader.GetFieldValue<DateOnly>(1),
            FieldType.DateTime => new DateTimeOffset(reader.GetFieldValue<DateTime>(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field.Type, "A field of this type cannot be sensitive."),
        };
}
