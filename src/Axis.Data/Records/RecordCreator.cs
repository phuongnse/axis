using Axis.Configuration.Model;

namespace Axis.Data.Records;

/// <summary>
/// Sets the creator fields of a record that a signed-in user creates. A body never sets a creator
/// field, so the parsed values hold none.
/// </summary>
public static class RecordCreator
{
    /// <summary>
    /// Returns <paramref name="values"/> with each creator field of <paramref name="entity"/> set to
    /// <paramref name="userId"/>, or <paramref name="values"/> unchanged when no user is signed in,
    /// which leaves the creator fields <c>null</c>.
    /// </summary>
    public static IReadOnlyList<RecordValue> WithCreator(EntityModel entity, IReadOnlyList<RecordValue> values, string? userId)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);

        if (userId is null)
        {
            return values;
        }

        var creatorValues = entity.Fields.Where(field => field.Creator).Select(field => new RecordValue(field, userId)).ToList();
        return creatorValues.Count == 0 ? values : [.. values, .. creatorValues];
    }
}
