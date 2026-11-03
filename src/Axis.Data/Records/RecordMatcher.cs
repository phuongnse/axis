using Axis.Configuration.Model;
using Npgsql;

namespace Axis.Data.Records;

/// <summary>
/// Sets the matched references of a record. A <c>reference</c> field with <c>match</c> is set on every create and
/// update to the target record whose <c>match.field</c> equals the record's <c>match.from</c> value. Every write
/// path runs this one step: after the defaults and the creator fields, and before the computed fields and the
/// validations, so they see the matched reference. On an update, <c>from</c> comes from the values when they hold it,
/// and from the stored record otherwise. The field is <c>null</c> when <c>from</c> is <c>null</c> or empty text, or when
/// no target record has that value. The lookup reads as the system: it needs no grant and applies no record filter.
/// A value given for a matched field is replaced.
/// </summary>
public static class RecordMatcher
{
    /// <summary>Whether <paramref name="entity"/> has a field with <c>match</c>.</summary>
    public static bool HasMatches(EntityModel entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return entity.Fields.Any(field => field.Match is not null);
    }

    /// <summary>
    /// Returns <paramref name="values"/> with each matched field of <paramref name="entity"/> set to the id of its
    /// target record, or <c>null</c>, or <paramref name="values"/> unchanged when the entity has no matched field.
    /// <paramref name="stored"/> is the stored record of an update, and <see langword="null"/> on a create.
    /// </summary>
    public static async Task<IReadOnlyList<RecordValue>> WithMatchesAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        IReadOnlyList<RecordValue> values,
        Record? stored,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);

        var matched = entity.Fields.Where(field => field.Match is not null).ToList();
        if (matched.Count == 0)
        {
            return values;
        }

        var result = values.Where(value => !matched.Any(field => field.Name == value.Field.Name)).ToList();
        foreach (var field in matched)
        {
            var id = await FindAsync(connection, application, entity, field, values, stored, cancellationToken);
            result.Add(new RecordValue(field, id));
        }

        return result;
    }

    private static async Task<Guid?> FindAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        FieldModel field,
        IReadOnlyList<RecordValue> values,
        Record? stored,
        CancellationToken cancellationToken)
    {
        var match = field.Match!;
        var target = application.FindEntity(field.Target!.Id)!;
        var targetField = target.Fields.First(candidate => string.Equals(candidate.Name, match.Field, StringComparison.Ordinal));
        var from = entity.Fields.First(candidate => string.Equals(candidate.Name, match.From, StringComparison.Ordinal));

        RecordValue? lookup = null;
        if (values.FirstOrDefault(value => value.Field.Name == from.Name) is { } given)
        {
            if (given.Value is not null)
            {
                lookup = new RecordValue(targetField, given.Value);
            }
        }
        else if (stored is not null
            && RecordClrValues.TryFromStored(from, stored.Values.GetValueOrDefault(from.Name), out var clr)
            && clr is not null)
        {
            RecordComputer.ToRecordValue(targetField, clr, out lookup);
        }

        if (lookup is null || lookup.Value is "")
        {
            return null;
        }

        return await RecordQueries.FindByValueAsync(connection, target, lookup, cancellationToken) is { } found ? found.Id : null;
    }
}
