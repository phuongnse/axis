using Axis.Configuration.Model;
using Axis.Data.Naming;
using Npgsql;

namespace Axis.Data.Records;

/// <summary>
/// The one check of a module entity's lifecycle that every record write runs, inside the shared
/// storage writes, so no caller can skip it. A create must be in an initial state, a state change
/// must follow a transition whose guard is <c>true</c>, and a delete must be in a deletable state.
/// Each check returns <see langword="null"/> when the entity has no lifecycle or the write is
/// allowed. A refusal is keyed <c>/values/&lt;state field&gt;</c> and names the point of the contract
/// that refused it. Its message is the transition's text key, or a fixed English text.
/// </summary>
internal static class RecordLifecycle
{
    /// <summary>Checks that the state a create gives is an initial state.</summary>
    public static RecordWriteResult? CheckCreate(EntityModel entity, IReadOnlyList<RecordValue> values)
    {
        if (entity.Contract?.Lifecycle is not { } lifecycle)
        {
            return null;
        }

        var state = values.FirstOrDefault(value => value.Field.Name == lifecycle.Field)?.Value as string;
        if (state is not null && lifecycle.States.Any(candidate => candidate.Name == state && candidate.Initial))
        {
            return null;
        }

        return state is null
            ? Refused(lifecycle, "A record cannot be created without a state.", point: null)
            : Refused(lifecycle, $"A record cannot be created in the state '{state}'.", $"{PlainName(entity)}/states/{state}/initial");
    }

    /// <summary>
    /// Checks a state change. An update that does not send the state field is not checked. Otherwise it
    /// reads the stored record, which is <see cref="RecordWriteOutcome.NotFound"/> when there is none and
    /// <see cref="RecordWriteOutcome.StaleVersion"/> when its version is not <paramref name="version"/>.
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
        RecordCipher? cipher,
        CancellationToken cancellationToken)
    {
        if (entity.Contract?.Lifecycle is not { } lifecycle
            || values.FirstOrDefault(value => value.Field.Name == lifecycle.Field) is not { } sent)
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

        var from = stored.Values.GetValueOrDefault(lifecycle.Field)?.GetValue<string>();
        var to = sent.Value as string;
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return null;
        }

        var transition = lifecycle.Transitions.FirstOrDefault(candidate => from is not null && to is not null && candidate.To == to && candidate.From.Contains(from));
        if (transition is null)
        {
            return Refused(lifecycle, $"No transition goes from '{from}' to '{to}'.", point: null);
        }

        if (transition.Guard is not { } guard)
        {
            return null;
        }

        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var sets = await RecordSets.ReadAsync(connection, application, entity, stored, links, cancellationToken, cipher);
        return RecordValidator.GuardHolds(application, entity, stored, values, rows, guard, now, sets)
            ? null
            : Refused(
                lifecycle,
                transition.Message?.TextKey ?? $"The transition '{transition.Name}' does not allow this change.",
                $"{PlainName(entity)}/transitions/{transition.Name}");
    }

    /// <summary>
    /// Checks that the stored state is deletable. It reads the state with <c>FOR UPDATE</c>, so a
    /// parallel update cannot change it before the delete.
    /// </summary>
    public static async Task<RecordDeleteResult?> CheckDeleteAsync(
        NpgsqlConnection connection,
        EntityModel entity,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (entity.Contract?.Lifecycle is not { } lifecycle)
        {
            return null;
        }

        await using var command = new NpgsqlCommand(
            $"SELECT {EntityNaming.Quote(EntityNaming.Column(lifecycle.Field))} FROM {RecordQueries.Table(entity)} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id FOR UPDATE",
            connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new RecordDeleteResult(RecordDeleteOutcome.NotFound);
        }

        var state = await reader.IsDBNullAsync(0, cancellationToken) ? null : reader.GetString(0);
        if (state is not null && lifecycle.States.Any(candidate => candidate.Name == state && candidate.Deletable))
        {
            return null;
        }

        return new RecordDeleteResult(
            RecordDeleteOutcome.LifecycleRefused,
            Errors: Errors(lifecycle, $"A record cannot be deleted in the state '{state}'."),
            Point: state is null ? null : $"{PlainName(entity)}/states/{state}/deletable");
    }

    private static RecordWriteResult Refused(LifecycleModel lifecycle, string message, string? point) =>
        new(RecordWriteOutcome.LifecycleRefused, Errors: Errors(lifecycle, message), Point: point);

    private static SortedDictionary<string, string[]> Errors(LifecycleModel lifecycle, string message) =>
        new(StringComparer.Ordinal) { [$"/values/{lifecycle.Field}"] = [message] };

    private static string PlainName(EntityModel entity) => entity.Name[(entity.Name.LastIndexOf('/') + 1)..];
}
