using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

/// <summary>
/// A sensitive field's stored value is encrypted, so compilation refuses each use that needs the
/// plain value. Each use is refused with <see cref="DiagnosticCodes.SensitiveFieldUse"/> at the
/// path of the use, and the same configuration without <c>sensitive</c> raises no such diagnostic.
/// </summary>
public sealed class SensitiveUseCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";
    private const string CustomerId = "11111111-1111-4111-8111-111111111112";
    private const string LineId = "11111111-1111-4111-8111-111111111113";
    private const string DataSourceId = "99999999-9999-4999-8999-999999999991";
    private const string PolicyId = "12121212-1212-4212-8212-121212121201";
    private const string MigrationId = "7b3096e8-c14a-48f2-9a56-30bbd774e102";

    private const string DataSourceFile = "data-sources/orders.json";
    private const string OrderFile = "entities/order.json";

    private const string Title = """{ "name": "title", "type": "text" }""";

    private const string Texts = """
        { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "role.employee": "Employee" } }
        """;

    private const string Role = """
        { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "employee", "formatVersion": 1, "label": { "textKey": "role.employee" } }
        """;

    public static TheoryData<string, string, string> Uses => new()
    {
        { "a data source filter", DataSourceFile, "/filter" },
        { "a measure filter", DataSourceFile, "/aggregate/measures/0/filter" },
        { "a filter through a reference path", DataSourceFile, "/filter" },
        { "a filter over a child collection", DataSourceFile, "/filter" },
        { "a policy grant filter", "policies/requests.json", "/grants/0/filter" },
        { "a data source sort", DataSourceFile, "/sort" },
        { "a data source sort through a reference path", DataSourceFile, "/sort" },
        { "a group field", DataSourceFile, "/aggregate/groupBy/0" },
        { "a measure", DataSourceFile, "/aggregate/measures/0/field" },
        { "an index", OrderFile, "/indexes/0/fields/1" },
        { "unique", OrderFile, "/fields/5/unique" },
        { "searchable", OrderFile, "/fields/5/searchable" },
        { "a stored computed field", OrderFile, "/fields/5/expression" },
        { "a migration value", "migrations/0001-fill.json", "/changes/0/value" },
        { "a migration fill target", "migrations/0001-fill.json", "/changes/0/field" },
        { "a migration convert target", "migrations/0001-fill.json", "/changes/0/field" },
    };

    [Theory]
    [MemberData(nameof(Uses))]
    public void A_use_of_a_sensitive_field_is_refused_at_its_path(string use, string file, string path)
    {
        using var folder = Folder(use, sensitive: true);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics, candidate => candidate.Code == DiagnosticCodes.SensitiveFieldUse);
        Assert.Equal((DiagnosticCodes.SensitiveFieldUse, file, path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("is sensitive", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    public static TheoryData<string> UseNames => new(Uses.Select(row => row.Data.Item1));

    [Theory]
    [MemberData(nameof(UseNames))]
    public void The_same_use_of_a_field_that_is_not_sensitive_is_not_refused(string use)
    {
        using var folder = Folder(use, sensitive: false);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SensitiveFieldUse);
    }

    [Theory]
    [InlineData("a group field", "Order.secret")]
    [InlineData("a measure", "Order.cost")]
    [InlineData("a data source sort through a reference path", "Customer.ssn")]
    [InlineData("a filter through a reference path", "Customer.ssn")]
    [InlineData("a filter over a child collection", "Line.note")]
    public void The_message_names_the_entity_that_owns_the_field(string use, string name)
    {
        using var folder = Folder(use, sensitive: true);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics, candidate => candidate.Code == DiagnosticCodes.SensitiveFieldUse);
        Assert.Contains($"'{name}'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sensitive_field_that_is_only_stored_and_projected_still_compiles()
    {
        using var folder = Folder("a data source sort", sensitive: true)
            .With(DataSourceFile, DataSource("""[{ "name": "title", "path": "title" }, { "name": "secret", "path": "secret" }]""", ""","sort": "title" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "customer", "type": "reference", "target": "Customer", "sensitive": true }""")]
    [InlineData("""{ "name": "customer", "type": "references", "target": "Customer", "sensitive": true }""")]
    [InlineData("""{ "name": "customer", "type": "file", "sensitive": true }""")]
    public void A_sensitive_reference_references_or_file_field_is_refused_at_its_flag(string field)
    {
        using var folder = TypedFieldFolder(field);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SensitiveFieldUse, OrderFile, "/fields/1/sensitive"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("cannot be sensitive", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "customer", "type": "reference", "target": "Customer", "sensitive": false }""")]
    [InlineData("""{ "name": "customer", "type": "references", "target": "Customer", "sensitive": false }""")]
    [InlineData("""{ "name": "customer", "type": "file", "sensitive": false }""")]
    public void A_reference_references_or_file_field_that_is_not_sensitive_compiles(string field)
    {
        using var folder = TypedFieldFolder(field);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void A_sensitive_display_field_is_refused_at_the_display_field()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/customer.json", $$"""
                { "id": "{{CustomerId}}", "kind": "entity", "name": "Customer", "formatVersion": 1,
                  "displayField": "name", "fields": [ { "name": "name", "type": "text", "required": true, "sensitive": true } ] }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SensitiveFieldUse, "entities/customer.json", "/displayField"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("the display field", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    private static TemporaryFolder TypedFieldFolder(string field) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With(OrderFile, $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [ {{Title}}, {{field}} ] }
                """)
            .With("entities/customer.json", $$"""
                { "id": "{{CustomerId}}", "kind": "entity", "name": "Customer", "formatVersion": 1,
                  "displayField": "name", "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """);

    private static TemporaryFolder Folder(string use, bool sensitive)
    {
        var flag = sensitive ? ", \"sensitive\": true" : "";
        var secret = $$"""{ "name": "secret", "type": "text"{{flag}} }""";
        var cost = $$"""{ "name": "cost", "type": "decimal", "precision": 10, "scale": 2{{flag}} }""";

        // Field 5 of Order is the extra field of the use that needs one.
        var extra = use switch
        {
            "unique" => $$""", { "name": "code", "type": "text"{{flag}}, "unique": true }""",
            "searchable" => $$""", { "name": "memo", "type": "text"{{flag}}, "searchable": true }""",
            "a stored computed field" => """, { "name": "double", "type": "decimal", "precision": 10, "scale": 2, "expression": "cost * 2" }""",
            _ => "",
        };
        var indexes = use == "an index" ? """, "indexes": [{ "name": "titleSecret", "fields": ["title", "secret"] }]""" : "";

        var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With(OrderFile, $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    {{Title}},
                    {{secret}},
                    {{cost}},
                    { "name": "customer", "type": "reference", "target": "Customer" },
                    { "name": "lines", "type": "child-collection", "target": "Line" }{{extra}}
                  ]{{indexes}} }
                """)
            .With("entities/customer.json", $$"""
                { "id": "{{CustomerId}}", "kind": "entity", "name": "Customer", "formatVersion": 1,
                  "displayField": "name", "fields": [ { "name": "name", "type": "text", "required": true }, { "name": "ssn", "type": "text"{{flag}} } ] }
                """)
            .With("entities/line.json", $$"""
                { "id": "{{LineId}}", "kind": "entity", "name": "Line", "formatVersion": 1,
                  "fields": [ { "name": "note", "type": "text"{{flag}} } ] }
                """);

        const string ProjectedAll = """[{ "name": "title", "path": "title" }, { "name": "secret", "path": "secret" }, { "name": "cost", "path": "cost" }]""";
        const string Count = """{ "name": "n", "function": "count" }""";
        switch (use)
        {
            case "a data source filter":
                folder.With(DataSourceFile, DataSource(ProjectedAll, """, "filter": "secret == 'x'" """));
                break;
            case "a measure filter":
                folder.With(DataSourceFile, DataSource(
                    ProjectedAll,
                    $$""", "aggregate": { "groupBy": ["title"], "measures": [{ "name": "n", "function": "count", "filter": "secret == 'x'" }] } """));
                break;
            case "a filter through a reference path":
                folder.With(DataSourceFile, DataSource(ProjectedAll, """, "filter": "customer.ssn == 'x'" """));
                break;
            case "a filter over a child collection":
                folder.With(DataSourceFile, DataSource(ProjectedAll, """, "filter": "any(lines, note == 'x')" """));
                break;
            case "a policy grant filter":
                folder
                    .With("texts/en.json", Texts)
                    .With("roles/employee.json", Role)
                    .With("policies/requests.json", $$"""
                        { "id": "{{PolicyId}}", "kind": "policy", "name": "RequestAccess", "formatVersion": 1, "roles": ["employee"],
                          "grants": [{ "entity": "Order", "actions": ["read"], "filter": "secret == 'x'" }] }
                        """);
                break;
            case "a data source sort":
                folder.With(DataSourceFile, DataSource(ProjectedAll, """, "sort": "-secret" """));
                break;
            case "a data source sort through a reference path":
                folder.With(DataSourceFile, DataSource("""[{ "name": "ssn", "path": "customer.ssn" }]""", """, "sort": "ssn" """));
                break;
            case "a group field":
                folder.With(DataSourceFile, DataSource(
                    ProjectedAll, $$""", "aggregate": { "groupBy": ["secret"], "measures": [{{Count}}] } """));
                break;
            case "a measure":
                folder.With(DataSourceFile, DataSource(
                    ProjectedAll,
                    $$""", "aggregate": { "groupBy": ["title"], "measures": [{ "name": "total", "function": "sum", "field": "cost" }] } """));
                break;
            case "a migration value":
                folder.With("migrations/0001-fill.json", $$"""
                    { "id": "{{MigrationId}}", "kind": "migration", "name": "Fill", "formatVersion": 1, "entity": "Order",
                      "changes": [{ "type": "fill", "field": "title", "value": "secret" }] }
                    """);
                break;
            case "a migration fill target":
            case "a migration convert target":
                folder.With("migrations/0001-fill.json", $$"""
                    { "id": "{{MigrationId}}", "kind": "migration", "name": "Fill", "formatVersion": 1, "entity": "Order",
                      "changes": [{ "type": "{{(use == "a migration fill target" ? "fill" : "convert")}}", "field": "secret", "value": "title" }] }
                    """);
                break;
        }

        return folder;
    }

    private static string DataSource(string fields, string extra) =>
        $$"""{ "id": "{{DataSourceId}}", "kind": "dataSource", "name": "Orders", "formatVersion": 1, "entity": "Order", "fields": {{fields}}{{extra}} }""";
}
