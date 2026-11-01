using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;
using Npgsql;

namespace Axis.Data.Records;

/// <summary>
/// The one run of a module entity's core validations that every record create and update makes, inside the
/// shared storage writes, so no caller can skip it. It runs after the entity's own validations, which the
/// callers run first, and before the lifecycle check. Each check returns <see langword="null"/> when the
/// entity has no core validation or every one passes. A failure is <see cref="RecordWriteOutcome.Invalid"/>,
/// keyed <c>/values/&lt;field&gt;</c> with the validation's message key, and names the point
/// <c>&lt;entity&gt;/validations/&lt;name&gt;</c>.
/// </summary>
internal static class RecordCoreValidations
{
    /// <summary>Checks a create. <c>previous</c> is <c>null</c> for every field.</summary>
    public static async Task<RecordWriteResult?> CheckCreateAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows,
        IReadOnlyList<RecordLinks> links,
        RecordWriteContext context,
        RecordCipher? cipher,
        CancellationToken cancellationToken)
    {
        if (!HasValidations(entity))
        {
            return null;
        }

        return await RunAsync(connection, application, entity, stored: null, values, rows, links, context, cipher, cancellationToken);
    }

    /// <summary>
    /// Checks an update. It reads the stored record, which is <see cref="RecordWriteOutcome.NotFound"/> when
    /// there is none and <see cref="RecordWriteOutcome.StaleVersion"/> when its version is not
    /// <paramref name="version"/>.
    /// </summary>
    public static async Task<RecordWriteResult?> CheckUpdateAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        Guid id,
        long version,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows,
        IReadOnlyList<RecordLinks> links,
        RecordWriteContext context,
        RecordCipher? cipher,
        CancellationToken cancellationToken)
    {
        if (!HasValidations(entity))
        {
            return null;
        }

        var stored = await RecordQueries.GetAsync(connection, application, entity, id, cipher, cancellationToken);
        if (stored is null)
        {
            return new RecordWriteResult(RecordWriteOutcome.NotFound);
        }

        if (stored.Version != version)
        {
            return new RecordWriteResult(RecordWriteOutcome.StaleVersion);
        }

        return await RunAsync(connection, application, entity, stored, values, rows, links, context, cipher, cancellationToken);
    }

    private static bool HasValidations(EntityModel entity) => entity.Contract?.Validations.Count > 0;

    private static async Task<RecordWriteResult?> RunAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        Record? stored,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows,
        IReadOnlyList<RecordLinks> links,
        RecordWriteContext context,
        RecordCipher? cipher,
        CancellationToken cancellationToken)
    {
        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var sets = await RecordSets.ReadAsync(connection, application, entity, stored, links, cancellationToken, cipher);
        var errors = RecordValidator.ValidateCore(
            application,
            entity,
            stored,
            values,
            rows,
            now,
            sets,
            new ExpressionAccountability(context.AccountableUser, context.DecisionTask?.ToString("D")),
            out var point);
        return errors is null ? null : new RecordWriteResult(RecordWriteOutcome.Invalid, Errors: errors, Point: point);
    }
}
