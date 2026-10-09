using Axis.Configuration.Model;
using Axis.Data.DataSources;
using Microsoft.AspNetCore.WebUtilities;

namespace Axis.Server.DataSources;

/// <summary>
/// Reads the data source parameters of a rows request from the raw query string. Each parameter is
/// read under its exact declared name, because the framework's query collection ignores letter case.
/// An absent or empty parameter is not given. Errors are keyed by the declared name, and their
/// messages never repeat the request text.
/// </summary>
internal static class DataSourceParameterReader
{
    /// <summary>
    /// Returns one value per declared parameter, in declaration order, with a null value for one not
    /// given. Adds an error to <paramref name="errors"/> for every missing required, repeated or
    /// malformed parameter.
    /// </summary>
    internal static IReadOnlyList<DataSourceParameterValue> Read(
        QueryString query, DataSourceModel dataSource, SortedDictionary<string, string[]> errors)
    {
        var valuesByName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var pair in new QueryStringEnumerable(query.Value))
        {
            var name = pair.DecodeName().ToString();
            if (!valuesByName.TryGetValue(name, out var values))
            {
                valuesByName[name] = values = [];
            }

            values.Add(pair.DecodeValue().ToString());
        }

        var result = new List<DataSourceParameterValue>(dataSource.Parameters.Count);
        foreach (var parameter in dataSource.Parameters)
        {
            object? value = null;
            var values = valuesByName.GetValueOrDefault(parameter.Name);
            if (values is { Count: > 1 })
            {
                errors[parameter.Name] = ["Must be given at most once."];
            }
            else if (values is null || values[0].Length == 0)
            {
                if (parameter.Required)
                {
                    errors[parameter.Name] = ["Required."];
                }
            }
            else if (!DataSourceParameterValues.TryParse(parameter, values[0], out value))
            {
                errors[parameter.Name] = [Message(parameter.Type)];
            }

            result.Add(new DataSourceParameterValue(parameter, value));
        }

        return result;
    }

    private static string Message(FieldType type) => type switch
    {
        FieldType.Text => "Must be text without a null character.",
        FieldType.Integer => "Must be an integer within 64 bits.",
        FieldType.Decimal => "Must be a plain number without an exponent.",
        FieldType.Boolean => "Must be true or false.",
        FieldType.Date => "Must be a date as yyyy-MM-dd.",
        FieldType.DateTime => "Must be an RFC 3339 date-time with an offset.",
        FieldType.Enum => "Must be one of the parameter's values.",
        FieldType.Reference => "Must be a hyphenated UUID.",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No parameter message for this type."),
    };
}
