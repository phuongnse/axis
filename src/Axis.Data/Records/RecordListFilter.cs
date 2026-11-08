using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Npgsql;

namespace Axis.Data.Records;

/// <summary>
/// The <c>filter</c> of a record list: conditions in nested groups of All or Any, which keep only the
/// records that match them. The format and the operators are in the record API reference, under paging
/// and sorting. A condition compares one field of a kind that has a column, is not sensitive and that
/// the user may read. Every value is a parameter, never part of the SQL.
/// </summary>
public sealed class RecordListFilter
{
    /// <summary>The most conditions and groups that one filter holds.</summary>
    public const int MaxNodes = 50;

    /// <summary>The most characters that a text value holds.</summary>
    public const int MaxTextLength = 200;

    internal const string Malformed = "Must be a JSON group {\"all\": [...]} or {\"any\": [...]} of groups and conditions {\"field\", \"op\", \"value\"}, with at most 50 of them.";

    internal const string UnusableField = "Must name a text, enum, number or date field that you may read and that is not sensitive.";

    internal const string UnknownOperator = "Must be one of eq, ne, contains, lt, lte, gt, gte, empty or notEmpty.";

    internal const string OperatorOfAnotherKind = "Is not an operator of the field's kind.";

    internal static readonly string TextTooLong = $"Must be a text value of at most {MaxTextLength} characters.";

    private const string All = "all";

    private const string Any = "any";

    private static readonly IReadOnlySet<string> _text = new HashSet<string>(["text"], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> _everyKind = new HashSet<string>(["text", "enum", "number", "date"], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> _ordered = new HashSet<string>(["number", "date"], StringComparer.Ordinal);

    private readonly Group _root;

    private readonly List<Condition> _parameters;

    private RecordListFilter(Group root, List<Condition> parameters, IReadOnlyList<FieldModel> fields)
    {
        _root = root;
        _parameters = parameters;
        Fields = fields;
    }

    /// <summary>
    /// The operators and the kinds of field that take each: <c>text</c>, <c>enum</c>, <c>number</c> for integer,
    /// decimal and money fields, and <c>date</c> for date and date-time fields.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Operators { get; } = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
    {
        ["eq"] = _everyKind,
        ["ne"] = _everyKind,
        ["contains"] = _text,
        ["lt"] = _ordered,
        ["lte"] = _ordered,
        ["gt"] = _ordered,
        ["gte"] = _ordered,
        ["empty"] = _everyKind,
        ["notEmpty"] = _everyKind,
    };

    /// <summary>The fields that the conditions compare, each once, in declaration order.</summary>
    public IReadOnlyList<FieldModel> Fields { get; }

    /// <summary>
    /// Parses <paramref name="json"/> against <paramref name="entity"/>. A group without a condition is left out, so a filter
    /// that ends empty means no filter: it succeeds with a null <paramref name="filter"/>. Field names match exactly. A field
    /// that does not exist, that <paramref name="canRead"/> refuses, that is sensitive or that has no column or a kind
    /// without conditions gets one message. <paramref name="error"/> never holds text of <paramref name="json"/>.
    /// </summary>
    public static bool TryParse(
        string json, EntityModel entity, Func<string, bool> canRead, out RecordListFilter? filter, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(canRead);

        filter = null;
        var parser = new Parser(entity, canRead);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            error = Malformed;
            return false;
        }

        using (document)
        {
            if (parser.ReadGroup(document.RootElement) is not { } root)
            {
                error = parser.Error ?? Malformed;
                return false;
            }

            error = null;
            if (Parser.PruneEmpty(root))
            {
                var names = parser.Conditions.Select(condition => condition.Field.Name).ToHashSet(StringComparer.Ordinal);
                filter = new RecordListFilter(root, parser.Parameters, [.. entity.Fields.Where(field => names.Contains(field.Name))]);
            }

            return true;
        }
    }

    /// <summary>The condition, wrapped in parentheses, that the row aliased <paramref name="rowAlias"/> matches. Its values are <c>@lf0</c>, <c>@lf1</c>, ….</summary>
    internal string Sql(string rowAlias) => _root.Sql(rowAlias);

    /// <summary>Adds the parameter of each condition that compares a value to <paramref name="command"/>.</summary>
    public void AddTo(NpgsqlCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        foreach (var condition in _parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(condition.ParameterName, RecordCommands.ParameterType(
                condition.Operator == "contains" ? FieldType.Text : condition.Field.Type))
            {
                Value = condition.Operator == "contains" ? RecordQueries.SearchPattern((string)condition.Value!) : condition.Value,
            });
        }
    }

    private static string? Kind(FieldType type) => type switch
    {
        FieldType.Text => "text",
        FieldType.Enum => "enum",
        FieldType.Integer or FieldType.Decimal or FieldType.Money => "number",
        FieldType.Date or FieldType.DateTime => "date",
        _ => null,
    };

    private abstract record Node
    {
        public abstract string Sql(string rowAlias);
    }

    private sealed record Group(bool IsAll, List<Node> Children) : Node
    {
        public override string Sql(string rowAlias) =>
            $"({string.Join(IsAll ? " AND " : " OR ", Children.Select(child => child.Sql(rowAlias)))})";
    }

    // Index is the position among the conditions that compare a value, or -1 for empty and notEmpty.
    private sealed record Condition(FieldModel Field, string Operator, object? Value, int Index) : Node
    {
        public string ParameterName => $"lf{Index.ToString(CultureInfo.InvariantCulture)}";

        public override string Sql(string rowAlias)
        {
            var column = $"{EntityNaming.Quote(rowAlias)}.{EntityNaming.Quote(EntityNaming.Column(Field.Name))}";
            var placeholder = FieldTypes.HoldsDecimal(Field.Type) ? $"@{ParameterName}::numeric" : $"@{ParameterName}";
            var text = Field.Type == FieldType.Text;
            return Operator switch
            {
                "eq" => $"{column} = {placeholder}",
                "ne" => $"{column} IS DISTINCT FROM {placeholder}",
                "contains" => RecordQueries.SearchTerm(column, placeholder),
                "lt" => $"{column} < {placeholder}",
                "lte" => $"{column} <= {placeholder}",
                "gt" => $"{column} > {placeholder}",
                "gte" => $"{column} >= {placeholder}",
                "empty" => text ? $"({column} IS NULL OR {column} = '')" : $"{column} IS NULL",
                "notEmpty" => text ? $"({column} IS NOT NULL AND {column} <> '')" : $"{column} IS NOT NULL",
                _ => throw new InvalidOperationException("Unknown operator."),
            };
        }
    }

    private sealed class Parser(EntityModel entity, Func<string, bool> canRead)
    {
        private int _nodes;

        public string? Error { get; private set; }

        // The conditions that compare a value, in parse order.
        public List<Condition> Parameters { get; } = [];

        // Every condition, whether it compares a value or not.
        public List<Condition> Conditions { get; } = [];

        // Reads the root of a filter, or a node that holds All or Any. Returns null with Error set when it is malformed.
        public Group? ReadGroup(JsonElement element)
        {
            if (!Count() || element.ValueKind != JsonValueKind.Object)
            {
                return Fail<Group>(Malformed);
            }

            var properties = element.EnumerateObject().ToList();
            if (properties is not [{ } property] || property.Name is not (All or Any) || property.Value.ValueKind != JsonValueKind.Array)
            {
                return Fail<Group>(Malformed);
            }

            var group = new Group(property.Name == All, []);
            foreach (var child in property.Value.EnumerateArray())
            {
                Node? node = child.ValueKind == JsonValueKind.Object && child.EnumerateObject().Any(member => member.Name is All or Any)
                    ? ReadGroup(child)
                    : ReadCondition(child);
                if (node is null)
                {
                    return null;
                }

                group.Children.Add(node);
            }

            return group;
        }

        private Condition? ReadCondition(JsonElement element)
        {
            if (!Count() || element.ValueKind != JsonValueKind.Object)
            {
                return Fail<Condition>(Malformed);
            }

            JsonElement? field = null;
            JsonElement? op = null;
            JsonElement? value = null;
            foreach (var property in element.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "field" when field is null:
                        field = property.Value;
                        break;
                    case "op" when op is null:
                        op = property.Value;
                        break;
                    case "value" when value is null:
                        value = property.Value;
                        break;
                    default:
                        return Fail<Condition>(Malformed);
                }
            }

            if (field is not { ValueKind: JsonValueKind.String } || op is not { ValueKind: JsonValueKind.String })
            {
                return Fail<Condition>(Malformed);
            }

            var column = RecordQueries.Columns(entity).FirstOrDefault(candidate =>
                !candidate.Sensitive && string.Equals(candidate.Name, field.Value.GetString(), StringComparison.Ordinal));
            if (column is null || !canRead(column.Name) || Kind(column.Type) is not { } kind)
            {
                return Fail<Condition>(UnusableField);
            }

            var name = op.Value.GetString()!;
            if (!Operators.TryGetValue(name, out var kinds))
            {
                return Fail<Condition>(UnknownOperator);
            }

            if (!kinds.Contains(kind))
            {
                return Fail<Condition>(OperatorOfAnotherKind);
            }

            // Empty and not empty take no value, and the other operators need one that is not null.
            var comparesValue = name is not ("empty" or "notEmpty");
            if (comparesValue ? value is null or { ValueKind: JsonValueKind.Null } : value is not null)
            {
                return Fail<Condition>(Malformed);
            }

            if (!comparesValue)
            {
                var unvalued = new Condition(column, name, null, -1);
                Conditions.Add(unvalued);
                return unvalued;
            }

            if (kind == "text" && value!.Value.ValueKind == JsonValueKind.String && value.Value.GetString()!.Length > MaxTextLength)
            {
                return Fail<Condition>(TextTooLong);
            }

            if (RecordInputParser.ReadComparedValue(value!.Value, column, out var compared) is { } message)
            {
                return Fail<Condition>(message);
            }

            var condition = new Condition(column, name, compared, Parameters.Count);
            Parameters.Add(condition);
            Conditions.Add(condition);
            return condition;
        }

        // Drops the groups that hold no condition, deepest first. Returns whether the group still holds a node.
        public static bool PruneEmpty(Group group)
        {
            group.Children.RemoveAll(child => child is Group inner && !PruneEmpty(inner));
            return group.Children.Count > 0;
        }

        // Counts a node, and fails when the filter holds more than MaxNodes of them.
        private bool Count()
        {
            if (++_nodes <= MaxNodes)
            {
                return true;
            }

            Fail<Node>(Malformed);
            return false;
        }

        private T? Fail<T>(string message)
            where T : class
        {
            Error ??= message;
            return null;
        }
    }
}
