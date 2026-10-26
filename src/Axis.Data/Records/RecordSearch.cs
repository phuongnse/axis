using Axis.Configuration.Model;
using Axis.Data.Naming;
using Npgsql;

namespace Axis.Data.Records;

/// <summary>
/// A record search: the text and the text fields it looks in. A record matches when any of the
/// fields contains the text, ignoring letter case.
/// </summary>
public sealed class RecordSearch(string text, IReadOnlyList<FieldModel> fields)
{
    /// <summary>The text to find. It is matched as plain text, never as a pattern.</summary>
    public string Text { get; } = text;

    /// <summary>The fields to look in.</summary>
    public IReadOnlyList<FieldModel> Fields { get; } = fields;

    /// <summary>
    /// The fields a search of <paramref name="entity"/> looks in, in declaration order: the text
    /// fields with a column that are searchable or the display field, and that <paramref name="canRead"/>
    /// allows.
    /// </summary>
    public static IReadOnlyList<FieldModel> Searched(EntityModel entity, Func<string, bool> canRead)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(canRead);

        return
        [
            .. entity.Fields.Where(field =>
                field.Type == FieldType.Text
                && field.HasColumn
                && (field.Searchable || string.Equals(field.Name, entity.DisplayField, StringComparison.OrdinalIgnoreCase))
                && canRead(field.Name)),
        ];
    }

    /// <summary>The condition, wrapped in parentheses, that any field of the row aliased <paramref name="rowAlias"/> contains <c>@search</c>.</summary>
    internal string Sql(string rowAlias) =>
        $"({string.Join(" OR ", Fields.Select(field => RecordQueries.SearchTerm($"{EntityNaming.Quote(rowAlias)}.{EntityNaming.Quote(EntityNaming.Column(field.Name))}")))})";

    /// <summary>Adds the <c>@search</c> parameter to <paramref name="command"/>.</summary>
    public void AddTo(NpgsqlCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Parameters.Add(RecordQueries.SearchParameter(Text));
    }
}
