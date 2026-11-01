using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;
using Npgsql;

namespace Axis.Data.Records;

/// <summary>
/// The run of the effects of a transition that every record update makes, inside the shared storage write,
/// so no caller can skip it. Each effect of the contract that is declared <c>on</c> the transition creates
/// its record in declaration order, through <see cref="RecordCommands.CreateFromAsync"/>, so the record
/// gets its defaults, computed fields, sequence numbers, validations and its own entity's contract. Its
/// creator fields hold the accountable user, and no grant is checked. A value that cannot be evaluated or held
/// exactly, and any create that is not written, refuse the effect with the point
/// <c>&lt;entity&gt;/effects/&lt;name&gt;</c>. Only an update calls this, so effects never chain.
/// </summary>
internal static class RecordEffects
{
    /// <summary>
    /// Runs the effects of <paramref name="transition"/> after <paramref name="written"/> is stored.
    /// <paramref name="stored"/> is the record before the write, which <c>previous</c> reads. Returns the
    /// refusal of the first effect that cannot be written, or the records the effects created.
    /// </summary>
    public static async Task<(RecordWriteResult? Refusal, IReadOnlyList<EffectRecord> Created)> RunAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        Guid releaseId,
        RecordWriteContext context,
        LifecycleTransitionModel transition,
        Record stored,
        Record written,
        RecordCipher? cipher,
        CancellationToken cancellationToken)
    {
        var effects = (entity.Contract?.Effects ?? []).Where(effect => string.Equals(effect.On, transition.Name, StringComparison.Ordinal)).ToList();
        if (effects.Count == 0)
        {
            return (null, []);
        }

        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var sets = await RecordSets.ReadAsync(connection, application, entity, written, [], cancellationToken, cipher);
        var current = BuildScope(application, entity, written, sets);
        var before = RecordValidator.BuildRecord(entity, stored, []);
        var accountability = new ExpressionAccountability(context.AccountableUser, context.DecisionTask?.ToString("D"));
        var created = new List<EffectRecord>();
        foreach (var effect in effects)
        {
            var point = $"{RecordLifecycle.PlainName(entity)}/effects/{effect.Name}";
            var target = application.FindEntity(effect.Entity.Id);
            if (target is null
                || current is null
                || before.Values.Any(pair => !pair.Exact)
                || Values(target, effect, current, before, now, application, accountability) is not { } values)
            {
                return (new RecordWriteResult(RecordWriteOutcome.EffectRefused, Point: point), []);
            }

            var result = await RecordCommands.CreateFromAsync(connection, application, target, releaseId, context, values, context.AccountableUser, now, cipher, cancellationToken);
            if (result is not { Outcome: RecordWriteOutcome.Written, Record: { } record })
            {
                return (new RecordWriteResult(RecordWriteOutcome.EffectRefused, Point: point), []);
            }

            created.Add(new EffectRecord(point, target, record));
        }

        return (null, created);
    }

    /// <summary>
    /// The record after the write as the interpreter reads it, with its child rows and sets, and its own id last
    /// so it wins over a field named <c>id</c>. It is <see langword="null"/> when a value cannot be held exactly.
    /// </summary>
    private static Dictionary<string, (object? Value, bool Exact)>? BuildScope(ApplicationModel application, EntityModel entity, Record written, RecordSets sets)
    {
        var record = RecordValidator.BuildRecord(entity, written, []);
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        if (!RecordCollections.TryAdd(application, entity, written, [], record, errors, sets) || record.Values.Any(pair => !pair.Exact))
        {
            return null;
        }

        record["id"] = (written.Id, true);
        return record;
    }

    /// <summary>
    /// Evaluates the values of an effect, or returns <see langword="null"/> when a field is unknown or a value
    /// stops with a run-time error.
    /// </summary>
    private static List<KeyValuePair<FieldModel, object?>>? Values(
        EntityModel target,
        ContractEffectModel effect,
        Dictionary<string, (object? Value, bool Exact)> current,
        Dictionary<string, (object? Value, bool Exact)> before,
        DateTimeOffset now,
        ApplicationModel application,
        ExpressionAccountability accountability)
    {
        var subject = new ExpressionValues(current.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value)));
        var previous = new ExpressionValues(before.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value)));
        var values = new List<KeyValuePair<FieldModel, object?>>();
        foreach (var assignment in effect.Values)
        {
            if (!target.TryGetField(assignment.Field, out var field))
            {
                return null;
            }

            var result = ExpressionInterpreter.Evaluate(
                assignment.Value.Syntax,
                assignment.Value.Check,
                subject,
                records: null,
                now: now,
                timeZone: application.TimeZone,
                previous: previous,
                accountability: accountability);
            if (!result.Succeeded)
            {
                return null;
            }

            values.Add(KeyValuePair.Create(field, result.Value));
        }

        return values;
    }
}
