using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Data.Records;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

public sealed class RecordListFilterTests
{
    private static readonly EntityModel _entity = Entity(
        Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a21"),
        "Item",
        "entities/item.json",
        Field("name", FieldType.Text),
        Field("quantity", FieldType.Integer),
        Field("status", FieldType.Enum, values: ["open", "closed"]),
        Field("neededBy", FieldType.Date),
        Field("flag", FieldType.Boolean),
        Field("note", FieldType.Text) with { Sensitive = true },
        Field("lines", FieldType.ChildCollection, target: Entity(Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a22"), "Line", "entities/line.json")));

    private static bool Parses(string json, out RecordListFilter? filter, out string? error, Func<string, bool>? canRead = null) =>
        RecordListFilter.TryParse(json, _entity, canRead ?? (_ => true), out filter, out error);

    private static string Refusal(string json, Func<string, bool>? canRead = null)
    {
        Assert.False(Parses(json, out var filter, out var error, canRead));
        Assert.Null(filter);
        return error!;
    }

    [Fact]
    public void Nested_groups_parse_to_parenthesised_conditions_in_parse_order()
    {
        Assert.True(Parses(
            """{"all":[{"field":"status","op":"eq","value":"open"},{"any":[{"field":"quantity","op":"gte","value":5},{"field":"name","op":"empty"},{"all":[{"field":"neededBy","op":"lt","value":"2026-01-01"}]}]}]}""",
            out var filter,
            out var error));

        Assert.Null(error);
        var neededBy = Axis.Data.Naming.EntityNaming.Column("neededBy");
        Assert.Equal(
            $"(\"r\".\"f_status\" = @lf0 AND (\"r\".\"f_quantity\" >= @lf1 OR (\"r\".\"f_name\" IS NULL OR \"r\".\"f_name\" = '') OR (\"r\".\"{neededBy}\" < @lf2)))",
            filter!.Sql("r"));
    }

    [Fact]
    public void Fields_are_each_named_once_in_declaration_order()
    {
        Assert.True(Parses(
            """{"any":[{"field":"status","op":"eq","value":"open"},{"field":"name","op":"contains","value":"a"},{"field":"status","op":"ne","value":"closed"}]}""",
            out var filter,
            out _));

        Assert.Equal(["name", "status"], filter!.Fields.Select(field => field.Name));
    }

    [Fact]
    public void Fields_include_those_of_empty_and_not_empty_conditions()
    {
        Assert.True(Parses(
            """{"all":[{"field":"status","op":"notEmpty"},{"field":"name","op":"empty"},{"any":[{"field":"quantity","op":"empty"}]}]}""",
            out var filter,
            out _));

        Assert.Equal(["name", "quantity", "status"], filter!.Fields.Select(field => field.Name));
    }

    [Theory]
    [InlineData("""{"all":[]}""")]
    [InlineData("""{"any":[{"all":[{"any":[]}]}]}""")]
    public void A_filter_without_a_condition_means_no_filter(string json)
    {
        Assert.True(Parses(json, out var filter, out var error));

        Assert.Null(filter);
        Assert.Null(error);
    }

    [Fact]
    public void An_empty_group_is_left_out_beside_a_condition()
    {
        Assert.True(Parses("""{"all":[{"any":[]},{"field":"name","op":"eq","value":"a"}]}""", out var filter, out _));

        Assert.Equal("(\"r\".\"f_name\" = @lf0)", filter!.Sql("r"));
    }

    [Fact]
    public void An_unknown_field_and_a_field_the_user_may_not_read_get_the_same_message()
    {
        var unknown = Refusal("""{"all":[{"field":"nonsense","op":"eq","value":"x"}]}""");
        var hidden = Refusal("""{"all":[{"field":"quantity","op":"eq","value":1}]}""", name => name != "quantity");

        Assert.Equal(unknown, hidden);
        Assert.True(Parses("""{"all":[{"field":"quantity","op":"eq","value":1}]}""", out _, out _));
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("note")]
    [InlineData("flag")]
    [InlineData("lines")]
    public void A_field_that_has_no_name_match_no_column_or_no_kind_of_condition_gets_the_field_message(string field)
    {
        var unknown = Refusal("""{"all":[{"field":"nonsense","op":"empty"}]}""");

        Assert.Equal(unknown, Refusal($$"""{"all":[{"field":"{{field}}","op":"empty"}]}"""));
    }

    [Theory]
    [InlineData("name", "lt", "\"a\"")]
    [InlineData("status", "contains", "\"o\"")]
    [InlineData("quantity", "contains", "\"1\"")]
    [InlineData("neededBy", "contains", "\"2026\"")]
    [InlineData("status", "gt", "\"open\"")]
    public void An_operator_that_does_not_fit_the_kind_is_refused(string field, string op, string value)
    {
        var message = Refusal($$"""{"all":[{"field":"{{field}}","op":"{{op}}","value":{{value}}}]}""");

        Assert.Equal(RecordListFilter.OperatorOfAnotherKind, message);
    }

    [Fact]
    public void An_unknown_operator_is_refused()
    {
        Assert.Equal(RecordListFilter.UnknownOperator, Refusal("""{"all":[{"field":"name","op":"like","value":"a"}]}"""));
    }

    [Theory]
    [InlineData("""{"all":[{"field":"status","op":"eq","value":"archived"}]}""")]
    [InlineData("""{"all":[{"field":"quantity","op":"eq","value":"1"}]}""")]
    [InlineData("""{"all":[{"field":"quantity","op":"eq","value":1.5}]}""")]
    [InlineData("""{"all":[{"field":"neededBy","op":"eq","value":"2026-13-01"}]}""")]
    [InlineData("""{"all":[{"field":"name","op":"eq","value":1}]}""")]
    [InlineData("""{"all":[{"field":"name","op":"eq","value":"a\u0000b"}]}""")]
    public void A_value_that_does_not_fit_the_field_is_refused_and_never_echoed(string json)
    {
        var message = Refusal(json);

        Assert.DoesNotContain("archived", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_text_value_over_the_length_limit_is_refused_and_the_limit_itself_is_not()
    {
        Assert.Equal(
            RecordListFilter.TextTooLong,
            Refusal($$"""{"all":[{"field":"name","op":"contains","value":"{{new string('a', RecordListFilter.MaxTextLength + 1)}}"}]}"""));
        Assert.True(Parses($$"""{"all":[{"field":"name","op":"contains","value":"{{new string('a', RecordListFilter.MaxTextLength)}}"}]}""", out _, out _));
    }

    [Fact]
    public void A_value_is_not_checked_against_the_limits_of_the_field()
    {
        var limited = _entity with { Fields = [Field("quantity", FieldType.Integer) with { Min = "1", Max = "10" }, Field("name", FieldType.Text, maxLength: 5) with { MinLength = 3 }] };

        Assert.True(RecordListFilter.TryParse("""{"all":[{"field":"quantity","op":"lt","value":0},{"field":"name","op":"contains","value":"ab"}]}""", limited, _ => true, out var filter, out _));
        Assert.NotNull(filter);
    }

    [Theory]
    [InlineData("""{"all":[{"field":"name","op":"eq","value":null}]}""")]
    [InlineData("""{"all":[{"field":"name","op":"eq"}]}""")]
    [InlineData("""{"all":[{"field":"name","op":"empty","value":"a"}]}""")]
    [InlineData("""{"all":[{"field":"name","op":"notEmpty","value":null}]}""")]
    [InlineData("""{"all":[{"field":"name","op":"eq","value":"a","extra":1}]}""")]
    [InlineData("""{"all":[{"field":"name","field":"status","op":"eq","value":"a"}]}""")]
    [InlineData("""{"all":[{"field":"name"}]}""")]
    [InlineData("""{"all":[{"field":1,"op":"eq","value":"a"}]}""")]
    [InlineData("""{"all":[1]}""")]
    [InlineData("""{"all":{}}""")]
    [InlineData("""{"all":[],"any":[]}""")]
    [InlineData("""{"all":[],"all":[]}""")]
    [InlineData("""{"none":[]}""")]
    [InlineData("""{"all":[{"all":[],"field":"name"}]}""")]
    [InlineData("""[]""")]
    [InlineData("""{"field":"name","op":"empty"}""")]
    [InlineData("""{"all":[""")]
    [InlineData("""not json""")]
    [InlineData("")]
    public void A_filter_that_is_malformed_gets_the_format_message(string json)
    {
        Assert.Equal(RecordListFilter.Malformed, Refusal(json));
    }

    [Fact]
    public void A_filter_of_more_than_fifty_conditions_and_groups_is_refused_and_fifty_are_not()
    {
        string Conditions(int count) => string.Join(",", Enumerable.Repeat("""{"field":"name","op":"empty"}""", count));

        Assert.True(Parses($$"""{"all":[{{Conditions(RecordListFilter.MaxNodes - 1)}}]}""", out _, out _));
        Assert.Equal(RecordListFilter.Malformed, Refusal($$"""{"all":[{{Conditions(RecordListFilter.MaxNodes)}}]}"""));
    }

    [Fact]
    public void A_filter_nested_deeper_than_the_reader_allows_is_malformed()
    {
        var json = string.Concat(Enumerable.Repeat("""{"all":[""", 40)) + string.Concat(Enumerable.Repeat("]}", 40));

        Assert.Equal(RecordListFilter.Malformed, Refusal(json));
    }

    [Fact]
    public void List_statement_holds_placeholders_and_none_of_the_values_of_the_filter()
    {
        Assert.True(Parses(
            """{"all":[{"field":"name","op":"eq","value":"O'Brien"},{"field":"name","op":"contains","value":"100%"}]}""", out var filter, out _));

        var statement = RecordQueries.ListStatement(_entity, null, null, null, filter);

        foreach (var sql in new[] { statement.CountSql, statement.PageSql })
        {
            Assert.Contains("@lf0", sql, StringComparison.Ordinal);
            Assert.Contains("lower(\"r\".\"f_name\") LIKE lower(@lf1)", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("O'Brien", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("100%", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Parameters_hold_the_values_and_the_escaped_pattern_of_contains()
    {
        Assert.True(Parses(
            """{"all":[{"field":"name","op":"ne","value":"a"},{"field":"name","op":"contains","value":"100%_"},{"field":"quantity","op":"lt","value":7}]}""", out var filter, out _));

        using var command = new Npgsql.NpgsqlCommand();
        filter!.AddTo(command);

        Assert.Equal(["lf0", "lf1", "lf2"], command.Parameters.Select(parameter => parameter.ParameterName));
        Assert.Equal(["a", "%100\\%\\_%", 7L], command.Parameters.Select(parameter => parameter.Value));
    }

    [Fact]
    public void Operators_hold_every_operator_the_parser_names_for_its_kinds()
    {
        Assert.Equal(["contains", "empty", "eq", "gt", "gte", "lt", "lte", "ne", "notEmpty"], RecordListFilter.Operators.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["text"], RecordListFilter.Operators["contains"].Order(StringComparer.Ordinal));
        Assert.Equal(["date", "number"], RecordListFilter.Operators["gte"].Order(StringComparer.Ordinal));
    }
}
