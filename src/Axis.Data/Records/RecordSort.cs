using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Model;

namespace Axis.Data.Records;

/// <summary>The order of a record list: one declared field, ascending or descending.</summary>
public sealed record RecordSort(FieldModel Field, bool Descending)
{
    /// <summary>
    /// Parses a declared field name, optionally preceded by one <c>-</c> for descending order.
    /// The name is matched exactly; <see cref="EntityModel.TryGetField"/> ignores letter case.
    /// </summary>
    public static bool TryParse(string text, EntityModel entity, [NotNullWhen(true)] out RecordSort? sort)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(entity);

        var descending = text.StartsWith('-');
        var name = descending ? text[1..] : text;
        var field = entity.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        sort = field is null ? null : new RecordSort(field, descending);
        return sort is not null;
    }
}
