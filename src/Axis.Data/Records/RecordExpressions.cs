using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;
using Axis.Expressions.Syntax;
using Npgsql;

namespace Axis.Data.Records;

/// <summary>
/// Evaluates a compiled expression, such as a process condition, against a stored record. A path
/// such as <c>department.manager.name</c> reads the records it names through
/// <see cref="RecordQueries"/>.
/// </summary>
public static class RecordExpressions
{
    /// <summary>
    /// Evaluates <paramref name="expression"/>, compiled over <paramref name="entity"/>, against
    /// <paramref name="record"/> as <see cref="RecordQueries.GetAsync"/> returns it. It first reads
    /// every record a path names, shortest path first, then evaluates without I/O. The reads run on
    /// <paramref name="connection"/>, so inside the caller's open transaction, with one command per
    /// record and each record read at most once. A <c>null</c> reference is not followed, and a
    /// reference that names no record gives <c>null</c>. A stored decimal that <see cref="decimal"/>
    /// cannot hold exactly, on the record or on a record a path reads, stops the evaluation with a
    /// <see cref="ExpressionRuntimeErrorKind.DecimalOverflow"/> error, as no value is rounded.
    /// <c>now()</c> gives <paramref name="now"/>, which is the start time of the caller's transaction.
    /// </summary>
    public static async Task<ExpressionEvaluationResult> EvaluateAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        Record record,
        ExpressionModel expression,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(expression);

        var subject = new Dictionary<string, (object? Value, bool Exact)>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in RecordQueries.Columns(entity))
        {
            if (!RecordClrValues.TryFromStored(field, record.Values.GetValueOrDefault(field.Name), out var value))
            {
                return Inexact(entity.Name, field.Name);
            }

            subject[field.Name] = (value, true);
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        if (!RecordCollections.TryAdd(application, entity, record, [], subject, errors))
        {
            // The first error is at /values/<collection>/<index>/<field>.
            var pointer = errors.Keys.First().Split('/');
            entity.TryGetField(pointer[2], out var collection);
            var child = application.FindEntity(collection!.Target!.Id)!;
            return Inexact(child.Name, pointer[4]);
        }

        // Each record read, by its entity's id and its own, or null when the reference names none.
        var loaded = new Dictionary<(Guid Entity, Guid Id), (Record Row, ExpressionValues Values)?>();

        // The record each path prefix reaches, such as `department` for `department.manager.name`.
        var reached = new Dictionary<string, (EntityModel Entity, Record Row)>(StringComparer.OrdinalIgnoreCase);

        // A prefix is read before the paths that go through it.
        var chains = expression.Check.PathTargets.Keys
            .Select(member => Chain(member.Target))
            .DistinctBy(chain => string.Join('.', chain), StringComparer.OrdinalIgnoreCase)
            .OrderBy(chain => chain.Count);
        foreach (var chain in chains)
        {
            (EntityModel Entity, Record Row) owner;
            if (chain.Count == 1)
            {
                owner = (entity, record);
            }
            else if (!reached.TryGetValue(string.Join('.', chain.Take(chain.Count - 1)), out owner))
            {
                // A null reference earlier in the path gives null, so there is nothing to read.
                continue;
            }

            if (!owner.Entity.TryGetField(chain[^1], out var reference) || reference.Type != FieldType.Reference)
            {
                throw new InvalidOperationException($"'{chain[^1]}' is not a reference field of '{owner.Entity.Name}'.");
            }

            if (owner.Row.Values.GetValueOrDefault(reference.Name) is not { } stored)
            {
                continue;
            }

            var id = Guid.Parse(stored.GetValue<string>());
            var target = application.FindEntity(reference.Target!.Id)
                ?? throw new InvalidOperationException("The application has no entity for the reference.");
            if (!loaded.TryGetValue((target.Id, id), out var found))
            {
                if (await RecordQueries.FindRowAsync(connection, target, id, cancellationToken) is { } row)
                {
                    var values = new List<KeyValuePair<string, object?>>();
                    foreach (var field in RecordQueries.Columns(target))
                    {
                        if (!RecordClrValues.TryFromStored(field, row.Values.GetValueOrDefault(field.Name), out var value))
                        {
                            return Inexact(target.Name, field.Name);
                        }

                        values.Add(KeyValuePair.Create(field.Name, value));
                    }

                    found = (row, new ExpressionValues(values));
                }

                loaded[(target.Id, id)] = found;
            }

            if (found is { } hit)
            {
                reached[string.Join('.', chain)] = (target, hit.Row);
            }
        }

        // The checker gives each step's target as written, so the entity is found ignoring letter case.
        ExpressionValues? Resolve(string name, Guid id) =>
            application.TryGetEntity(name, out var target) && loaded.TryGetValue((target.Id, id), out var found)
                ? found?.Values
                : throw new InvalidOperationException($"The record '{id}' of '{name}' was not read before the evaluation.");

        return ExpressionInterpreter.Evaluate(
            expression.Syntax,
            expression.Check,
            new ExpressionValues(subject.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value))),
            Resolve,
            now);
    }

    /// <summary>The field names of a path's target, from the subject's field to the last reference, such as <c>department, manager</c>.</summary>
    private static List<string> Chain(ExpressionNode node) => node switch
    {
        NameNode name => [name.Name],
        MemberNode member => [.. Chain(member.Target), member.Name],
        _ => throw new InvalidOperationException("A path starts at a field name."),
    };

    private static ExpressionEvaluationResult Inexact(string entity, string field) =>
        new(null, new ExpressionRuntimeError(
            ExpressionRuntimeErrorKind.DecimalOverflow,
            $"The stored value of '{entity}.{field}' cannot be held exactly as a decimal at character 1.",
            0));
}
