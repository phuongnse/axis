using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;

namespace Axis.Configuration.Tests;

public sealed class MatchCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";
    private const string LineId = "33333333-3333-4333-8333-333333333333";
    private const string EmployeeId = "55555555-5555-4555-8555-555555555555";
    private const string SequenceId = "44444444-4444-4444-8444-444444444444";

    private const int FieldIndex = 6;

    [Theory]
    [InlineData("user", "requester", "user", "requester")]
    [InlineData("USER", "Requester", "user", "requester")]
    public void Valid_match_is_on_the_field_model_with_the_declared_names(string field, string from, string expectedField, string expectedFrom)
    {
        using var folder = Folder($$"""{ "name": "requesterEmployee", "type": "reference", "target": "Employee", "match": { "field": "{{field}}", "from": "{{from}}" } }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.True(order.TryGetField("requesterEmployee", out var matched));
        Assert.Equal(new FieldMatchModel(expectedField, expectedFrom), matched.Match);
        Assert.All(order.Fields.Where(candidate => candidate.Name is not ("requesterEmployee" or "other")), candidate => Assert.Null(candidate.Match));
    }

    [Theory]
    [InlineData("""{ "field": "user" }""")]
    [InlineData("""{ "from": "requester" }""")]
    [InlineData("""{ "field": "user", "from": "requester", "extra": true }""")]
    public void Match_with_a_wrong_shape_breaks_the_schema(string match)
    {
        using var folder = Folder($$"""{ "name": "requesterEmployee", "type": "reference", "target": "Employee", "match": {{match}} }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SchemaViolation);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("missing", "requester")]
    [InlineData("nickname", "requester")]
    [InlineData("user", "missing")]
    [InlineData("user", "requesterEmployee")]
    [InlineData("user", "other")]
    [InlineData("user", "upper")]
    [InlineData("user", "secret")]
    [InlineData("code", "requester")]
    [InlineData("user", "owner")]
    public void Match_with_a_wrong_field_is_an_invalid_match(string field, string from)
    {
        using var folder = Folder(
            $$"""{ "name": "requesterEmployee", "type": "reference", "target": "Employee", "match": { "field": "{{field}}", "from": "{{from}}" } }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => (diagnostic.Code, diagnostic.Path) == (DiagnosticCodes.InvalidMatch, $"/fields/{FieldIndex}/match"));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "requesterEmployee", "type": "text", "match": { "field": "user", "from": "requester" } }""", null)]
    [InlineData("""{ "name": "requesterEmployee", "type": "reference", "target": "Employee", "required": true, "match": { "field": "user", "from": "requester" } }""", null)]
    [InlineData("""{ "name": "requesterEmployee", "type": "reference", "target": "Employee", "expression": "owner", "match": { "field": "user", "from": "requester" } }""", null)]
    [InlineData("""{ "name": "requesterEmployee", "type": "reference", "target": "Employee", "default": "owner", "match": { "field": "user", "from": "requester" } }""", null)]
    [InlineData("""{ "name": "requesterEmployee", "type": "reference", "target": "Employee", "sequence": "PurchaseRequestNumber", "match": { "field": "user", "from": "requester" } }""", null)]
    [InlineData(null, """{ "name": "requesterEmployee", "type": "reference", "target": "Employee", "match": { "field": "user", "from": "description" } }""")]
    public void Match_on_a_field_that_cannot_have_one_is_an_invalid_constraint(string? orderField, string? lineField)
    {
        using var folder = Folder(orderField, lineField);

        var result = ApplicationCompiler.Compile(folder.Path);

        var (file, index) = orderField is null ? ("entities/line.json", 2) : ("entities/order.json", FieldIndex);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path) == (DiagnosticCodes.InvalidConstraint, file, $"/fields/{index}/match"));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Process_cannot_set_a_matched_field()
    {
        using var folder = Folder(null)
            .With("processes/review.json", """
                { "id": "66666666-6666-4666-8666-666666666666", "kind": "process", "name": "Review", "formatVersion": 1, "entity": "Order", "start": "set",
                  "steps": [
                    { "name": "set", "type": "operation", "operation": "updateRecord", "set": { "other": "null" }, "next": "done" },
                    { "name": "done", "type": "end" }
                  ] }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.ReadOnlySetField, "/steps/0/set/other"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("is set by its match", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    /// <summary>
    /// An Employee entity with <c>fullName</c>, a unique text <c>user</c>, a text <c>nickname</c> and
    /// a unique integer <c>code</c>. An Order entity with <c>title</c> and a <c>lines</c> child
    /// collection, then <c>requester</c>, a sensitive <c>secret</c>, a computed <c>upper</c>, an
    /// <c>owner</c> reference to Employee, then <paramref name="orderField"/> at index 6 and a
    /// matched <c>other</c> at index 7. Its child entity Line has <c>description</c> and
    /// <c>quantity</c>, then <paramref name="lineField"/> at index 2.
    /// </summary>
    private static TemporaryFolder Folder(string? orderField, string? lineField = null) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/employee.json", $$"""
                { "id": "{{EmployeeId}}", "kind": "entity", "name": "Employee", "formatVersion": 1, "displayField": "fullName",
                  "fields": [
                    { "name": "fullName", "type": "text", "required": true },
                    { "name": "user", "type": "text", "unique": true },
                    { "name": "nickname", "type": "text" },
                    { "name": "code", "type": "integer", "unique": true }
                  ] }
                """)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "title", "type": "text" },
                    { "name": "lines", "type": "child-collection", "target": "Line" },
                    { "name": "requester", "type": "text" },
                    { "name": "secret", "type": "text", "sensitive": true },
                    { "name": "upper", "type": "text", "expression": "title" },
                    { "name": "owner", "type": "reference", "target": "Employee" }{{(orderField is null ? "" : $", {orderField}")}},
                    { "name": "other", "type": "reference", "target": "Employee", "match": { "field": "user", "from": "requester" } }
                  ] }
                """)
            .With("entities/line.json", $$"""
                { "id": "{{LineId}}", "kind": "entity", "name": "Line", "formatVersion": 1,
                  "fields": [
                    { "name": "description", "type": "text" },
                    { "name": "quantity", "type": "integer" }{{(lineField is null ? "" : $", {lineField}")}}
                  ] }
                """)
            .With("sequences/pr-number.json", $$"""
                { "id": "{{SequenceId}}", "kind": "sequence", "name": "PurchaseRequestNumber", "formatVersion": 1,
                  "format": "PR-{yyyy}-{n:5}" }
                """);
}
