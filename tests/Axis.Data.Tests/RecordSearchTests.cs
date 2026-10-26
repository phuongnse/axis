using Axis.Configuration.Model;
using Axis.Data.Records;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

public sealed class RecordSearchTests
{
    private static readonly EntityModel _entity = Entity(
        Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a11"),
        "Ticket",
        "entities/ticket.json",
        "title",
        Field("title", FieldType.Text),
        Field("note", FieldType.Text, searchable: true));

    [Theory]
    [InlineData("a%b_c\\d", "%a\\%b\\_c\\\\d%")]
    [InlineData("\\%", "%\\\\\\%%")]
    [InlineData("plain text", "%plain text%")]
    [InlineData("O'Brien", "%O'Brien%")]
    public void Pattern_wraps_the_text_in_percent_signs_and_escapes_the_characters_that_LIKE_reads(string text, string expected)
    {
        Assert.Equal(expected, RecordQueries.SearchPattern(text));
    }

    [Fact]
    public void Parameter_holds_the_escaped_pattern_as_text()
    {
        var parameter = RecordQueries.SearchParameter("50%_\\");

        Assert.Equal("search", parameter.ParameterName);
        Assert.Equal("%50\\%\\_\\\\%", parameter.Value);
    }

    [Fact]
    public void List_statement_matches_each_searched_field_with_LIKE_over_lower_and_never_strpos()
    {
        var search = new RecordSearch("zeph", RecordSearch.Searched(_entity, _ => true));

        var statement = RecordQueries.ListStatement(_entity, null, search, null);

        foreach (var sql in new[] { statement.CountSql, statement.PageSql })
        {
            Assert.Contains("lower(\"r\".\"f_title\") LIKE lower(@search) OR lower(\"r\".\"f_note\") LIKE lower(@search)", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("strpos", sql, StringComparison.OrdinalIgnoreCase);
        }
    }
}
