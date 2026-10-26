using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Policy;
using Npgsql;

namespace Axis.Processes.Records;

/// <summary>
/// Turns the display values in the reference cells of an import row into record ids. A cell of a reference
/// field that is not a record id is looked up by the display field of the field's target, as an exact match, in
/// every record of the target: the uploader's read filters on the target do not narrow it, as they do not hide a
/// label. The lookup runs only for a reference field whose target display field the uploader may read, and any
/// other cell is left for the record parser to check as a record id. An empty cell and a cell that is a record id
/// are left as they are. A value that matches no record, or more than one, is an error at
/// <c>/values/{field}</c> that does not repeat the value. Each distinct value of a field is looked up once, and
/// the answer is kept for the whole run.
/// </summary>
internal sealed class RecordImportReferences
{
    private const string NoMatch = "Matches no record by its display value.";
    private const string ManyMatches = "Matches more than one record by its display value.";

    private readonly (int Index, FieldModel Field, EntityModel Target)[] _columns;
    private readonly Dictionary<(string Field, string Value), IReadOnlyList<Guid>> _found = [];

    private RecordImportReferences((int Index, FieldModel Field, EntityModel Target)[] columns)
    {
        _columns = columns;
    }

    /// <summary>
    /// The lookups of the reference columns of <paramref name="columns"/> that <paramref name="roles"/> may read
    /// the target display field of.
    /// </summary>
    public static RecordImportReferences Create(ApplicationModel application, IEnumerable<string> roles, RecordImportColumns columns)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(columns);

        var held = roles.ToList();
        var lookups = new List<(int, FieldModel, EntityModel)>();
        for (var index = 0; index < columns.Fields.Count; index++)
        {
            if (columns.Fields[index] is { Type: FieldType.Reference } field
                && RecordFieldAccess.CanReadLabel(application, held, field)
                && application.FindEntity(field.Target!.Id) is { } target)
            {
                lookups.Add((index, field, target));
            }
        }

        return new RecordImportReferences([.. lookups]);
    }

    /// <summary>
    /// <paramref name="cells"/> with each display value in a looked-up column replaced by its record id, or the
    /// errors by pointer into the create body, and the cells as they were, when a value matches no record or more
    /// than one.
    /// </summary>
    public async Task<(IReadOnlyList<string> Cells, Dictionary<string, string[]>? Errors)> ResolveAsync(
        NpgsqlConnection connection,
        IReadOnlyList<string> cells,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(cells);

        string[]? resolved = null;
        Dictionary<string, string[]>? errors = null;
        foreach (var (index, field, target) in _columns)
        {
            var cell = cells[index];
            if (cell.Length == 0 || cell.Length == 36 && Guid.TryParseExact(cell, "D", out _))
            {
                continue;
            }

            var key = (field.Name, cell);
            if (!_found.TryGetValue(key, out var ids))
            {
                ids = await RecordQueries.FindByDisplayValueAsync(connection, target, cell, cancellationToken);
                _found[key] = ids;
            }

            if (ids.Count == 1)
            {
                resolved ??= [.. cells];
                resolved[index] = ids[0].ToString("D");
            }
            else
            {
                errors ??= new Dictionary<string, string[]>(StringComparer.Ordinal);
                errors[$"/values/{field.Name}"] = [ids.Count == 0 ? NoMatch : ManyMatches];
            }
        }

        return errors is null ? (resolved ?? cells, null) : (cells, errors);
    }
}
