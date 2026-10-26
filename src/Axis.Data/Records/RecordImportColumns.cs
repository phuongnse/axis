using System.Text.Json;
using Axis.Configuration.Model;

namespace Axis.Data.Records;

/// <summary>
/// The columns of an import file read against an entity: which field each header cell names, and how a row
/// of cells becomes the body of a create. The header names the fields of the entity's table, matched ignoring
/// letter case. A cell that names no such field, a field named twice, a child collection and a file field are errors
/// at <c>/columns/{n}</c>, with <c>n</c> counting the columns from 1. An empty cell leaves its field out of the
/// body, so its default applies or <c>Required.</c> is reported. A cell of an integer, decimal or money field
/// that is written as a JSON number is a number, the cell <c>true</c> or <c>false</c>, in any letter case, of
/// a boolean field is a boolean, and every other cell is text, so the record parser checks a row exactly as
/// it checks a JSON body.
/// </summary>
internal sealed class RecordImportColumns
{
    private readonly FieldModel?[] _fields;

    private RecordImportColumns(FieldModel?[] fields)
    {
        _fields = fields;
    }

    /// <summary>
    /// Maps the cells of <paramref name="header"/> to the fields of <paramref name="entity"/>. The result holds the
    /// columns, or the errors of the header by JSON Pointer in column order.
    /// </summary>
    public static (RecordImportColumns? Columns, Dictionary<string, string[]>? Errors) Map(IReadOnlyList<string> header, EntityModel entity)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(entity);

        var byName = new Dictionary<string, FieldModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in entity.Fields)
        {
            byName.TryAdd(field.Name, field);
        }

        var fields = new FieldModel?[header.Count];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        for (var index = 0; index < header.Count; index++)
        {
            var pointer = $"/columns/{index + 1}";
            if (!byName.TryGetValue(header[index], out var field))
            {
                errors[pointer] = ["Is not a field of this entity."];
            }
            else if (!field.HasColumn)
            {
                errors[pointer] = ["Is a collection, which an import cannot set."];
            }
            else if (field.Type == FieldType.File)
            {
                errors[pointer] = ["Is a file, which an import cannot set."];
            }
            else if (!seen.Add(field.Name))
            {
                errors[pointer] = ["Is named more than once."];
            }
            else
            {
                fields[index] = field;
            }
        }

        return errors.Count > 0 ? (null, errors) : (new RecordImportColumns(fields), null);
    }

    /// <summary>The field of each column, in column order.</summary>
    internal IReadOnlyList<FieldModel> Fields => _fields!;

    /// <summary>The body of a create, <c>{"values":{…}}</c> as UTF-8, for a row of <paramref name="cells"/>.</summary>
    public byte[] Body(IReadOnlyList<string> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("values");
            for (var index = 0; index < _fields.Length; index++)
            {
                var field = _fields[index]!;
                var cell = cells[index];
                if (cell.Length == 0)
                {
                    continue;
                }

                writer.WritePropertyName(field.Name);
                if (field.Type is FieldType.Integer or FieldType.Decimal or FieldType.Money && RecordImportFiles.IsJsonNumber(cell))
                {
                    writer.WriteRawValue(cell);
                }
                else if (field.Type == FieldType.Boolean && cell.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteBooleanValue(true);
                }
                else if (field.Type == FieldType.Boolean && cell.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteBooleanValue(false);
                }
                else
                {
                    writer.WriteStringValue(cell);
                }
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
