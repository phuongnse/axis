using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

/// <summary>
/// A policy grant's filter, a data source's filter and a measure's filter may test an exact lookup
/// field with <c>==</c> or <c>!=</c> against a literal, and the data source's filters also against a
/// data source parameter, because Axis runs that test against the stored lookup hashes. Every other
/// filter use of a sensitive field stays <see cref="DiagnosticCodes.SensitiveFieldUse"/>.
/// </summary>
public sealed class ExactLookupFilterCompilerTests
{
    private const string PersonId = "11111111-1111-4111-8111-111111111121";
    private const string DataSourceId = "99999999-9999-4999-8999-999999999992";
    private const string PolicyId = "12121212-1212-4212-8212-121212121202";

    private const string Texts = """
        { "id": "55555555-5555-4555-8555-555555555556", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "role.employee": "Employee" } }
        """;

    private const string Role = """
        { "id": "44444444-4444-4444-8444-444444444402", "kind": "role", "name": "employee", "formatVersion": 1, "label": { "textKey": "role.employee" } }
        """;

    [Theory]
    [InlineData("nationalId == 'x'")]
    [InlineData("nationalId != 'x'")]
    [InlineData("'x' == nationalId")]
    [InlineData("level == 5")]
    [InlineData("level != 5")]
    [InlineData("not (nationalId == 'x')")]
    [InlineData("nationalId == 'x' and name == 'y'")]
    public void An_exact_lookup_equality_in_a_policy_grant_filter_compiles(string filter)
    {
        using var folder = PolicyFolder(filter);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Theory]
    [InlineData("level > 1")]
    [InlineData("level >= 1")]
    [InlineData("nationalId is null")]
    [InlineData("nationalId is not null")]
    [InlineData("nationalId == null")]
    [InlineData("nationalId == name")]
    [InlineData("nationalId == nationalId")]
    [InlineData("nationalId in ('x', 'y')")]
    [InlineData("length(nationalId) == 3")]
    [InlineData("coalesce(nationalId, '') == 'x'")]
    [InlineData("if(nationalId == 'x', true, false)")]
    [InlineData("any(children, nationalId == 'x')")]
    public void Every_other_filter_use_of_an_exact_lookup_field_is_refused(string filter)
    {
        using var folder = PolicyFolder(filter);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics, candidate => candidate.Code == DiagnosticCodes.SensitiveFieldUse);
        Assert.Equal(("policies/people.json", "/grants/0/filter"), (diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_sensitive_field_without_an_exact_lookup_is_refused_for_the_same_equality()
    {
        using var folder = PolicyFolder("secret == 'x'");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics, candidate => candidate.Code == DiagnosticCodes.SensitiveFieldUse);
        Assert.Equal(("policies/people.json", "/grants/0/filter"), (diagnostic.File, diagnostic.Path));
    }

    [Theory]
    [InlineData("nationalId == id")]
    [InlineData("id == nationalId")]
    [InlineData("nationalId != id")]
    [InlineData("nationalId == 'x'")]
    [InlineData("level == n")]
    [InlineData("nationalId == id and name == 'y'")]
    public void An_exact_lookup_equality_in_a_data_source_filter_compiles(string filter)
    {
        using var folder = DataSourceFolder(filter);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Theory]
    [InlineData("level > 1")]
    [InlineData("nationalId == name")]
    [InlineData("bornOn == d")]
    [InlineData("nationalId == null")]
    [InlineData("nationalId == nationalId")]
    [InlineData("length(nationalId) == 1")]
    [InlineData("nationalId == id or nationalId is null")]
    [InlineData("secret == id")]
    public void Every_other_data_source_filter_use_of_an_exact_lookup_field_is_refused(string filter)
    {
        using var folder = DataSourceFolder(filter);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics, candidate => candidate.Code == DiagnosticCodes.SensitiveFieldUse);
        Assert.Equal(("data-sources/people.json", "/filter"), (diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("nationalId == 'x'", null)]
    [InlineData("nationalId != id", null)]
    [InlineData("nationalId == name", DiagnosticCodes.SensitiveFieldUse)]
    [InlineData("nationalId is not null", DiagnosticCodes.SensitiveFieldUse)]
    public void A_measure_filter_may_test_an_exact_lookup_field_in_the_same_way(string filter, string? code)
    {
        using var folder = DataSourceFolder(
            null,
            $$"""{ "groupBy": ["name"], "measures": [{ "name": "people", "function": "count", "filter": "{{filter}}" }] }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        if (code is null)
        {
            Assert.Empty(result.Diagnostics);
            Assert.NotNull(result.Model);
            return;
        }

        var diagnostic = Assert.Single(result.Diagnostics, candidate => candidate.Code == code);
        Assert.Equal(("data-sources/people.json", "/aggregate/measures/0/filter"), (diagnostic.File, diagnostic.Path));
    }

    private static TemporaryFolder DataSourceFolder(string? filter, string? aggregate = null)
    {
        var filterMember = filter is null ? "" : ", \"filter\": \"" + filter + "\"";
        var aggregateMember = aggregate is null ? "" : ", \"aggregate\": " + aggregate;
        return PolicyFolder("name == 'x'")
            .With("data-sources/people.json", $$"""
                { "id": "{{DataSourceId}}", "kind": "dataSource", "name": "People", "formatVersion": 1, "entity": "Person",
                  "fields": [{ "name": "name", "path": "name" }],
                  "parameters": [{ "name": "id", "type": "text" }, { "name": "n", "type": "integer" }, { "name": "d", "type": "date" }]
                  {{filterMember}}{{aggregateMember}} }
                """);
    }

    private static TemporaryFolder PolicyFolder(string filter) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/person.json", $$"""
                { "id": "{{PersonId}}", "kind": "entity", "name": "Person", "formatVersion": 1,
                  "fields": [
                    { "name": "name", "type": "text" },
                    { "name": "nationalId", "type": "text", "sensitive": true, "exactLookup": true },
                    { "name": "secret", "type": "text", "sensitive": true },
                    { "name": "level", "type": "integer", "sensitive": true, "exactLookup": true },
                    { "name": "bornOn", "type": "date", "sensitive": true, "exactLookup": true },
                    { "name": "children", "type": "child-collection", "target": "Child" }
                  ] }
                """)
            .With("entities/child.json", $$"""
                { "id": "11111111-1111-4111-8111-111111111122", "kind": "entity", "name": "Child", "formatVersion": 1,
                  "fields": [ { "name": "nationalId", "type": "text", "sensitive": true, "exactLookup": true } ] }
                """)
            .With("texts/en.json", Texts)
            .With("roles/employee.json", Role)
            .With("policies/people.json", $$"""
                { "id": "{{PolicyId}}", "kind": "policy", "name": "PersonAccess", "formatVersion": 1, "roles": ["employee"],
                  "grants": [{ "entity": "Person", "actions": ["read"], "filter": "{{filter}}" }] }
                """);
}
