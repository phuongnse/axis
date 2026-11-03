using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class UserIdCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";
    private const string LineId = "33333333-3333-4333-8333-333333333333";
    private const string SequenceId = "44444444-4444-4444-8444-444444444444";

    [Fact]
    public void Valid_user_id_field_is_on_the_field_model()
    {
        using var folder = Folder("""{ "name": "manager", "type": "text", "maxLength": 100, "userId": true }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.True(order.TryGetField("manager", out var manager));
        Assert.True(manager.UserId);
        Assert.All(order.Fields.Where(field => field.Name != "manager"), field => Assert.False(field.UserId));
    }

    [Theory]
    [InlineData("""{ "name": "manager", "type": "text", "required": true, "userId": true }""")]
    [InlineData("""{ "name": "manager", "type": "text", "creator": true, "userId": false }""")]
    [InlineData("""{ "name": "manager", "type": "text", "expression": "title", "userId": false }""")]
    public void User_id_that_cannot_clash_compiles(string orderField)
    {
        using var folder = Folder(orderField);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "manager", "type": "integer", "userId": true }""", null, "integer")]
    [InlineData("""{ "name": "manager", "type": "integer", "userId": false }""", null, "integer")]
    [InlineData("""{ "name": "manager", "type": "text", "creator": true, "userId": true }""", null, "creator")]
    [InlineData("""{ "name": "manager", "type": "text", "expression": "title", "userId": true }""", null, "expression")]
    [InlineData("""{ "name": "manager", "type": "text", "sequence": "PurchaseRequestNumber", "userId": true }""", null, "sequence")]
    [InlineData(null, """{ "name": "manager", "type": "text", "userId": true }""", "Order.lines")]
    public void User_id_on_a_field_that_cannot_have_one_is_an_invalid_constraint(string? orderField, string? lineField, string messagePart)
    {
        using var folder = Folder(orderField, lineField);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        var (file, resourceId) = orderField is null ? ("entities/line.json", LineId) : ("entities/order.json", OrderId);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, file, "/fields/2/userId"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse(resourceId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    /// <summary>
    /// An Order entity with <c>title</c> and a <c>lines</c> child collection, then
    /// <paramref name="orderField"/> at index 2. Its child entity Line has <c>description</c> and
    /// <c>quantity</c>, then <paramref name="lineField"/> at index 2. The PurchaseRequestNumber
    /// sequence is loaded.
    /// </summary>
    private static TemporaryFolder Folder(string? orderField, string? lineField = null) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "title", "type": "text" },
                    { "name": "lines", "type": "child-collection", "target": "Line" }{{(orderField is null ? "" : $", {orderField}")}}
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
