using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Expressions.Parsing;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Tests;

public sealed class ComputedFingerprintsTests
{
    private static readonly Guid _orderId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid _lineId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private const string Manifest = """
        { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }
        """;

    [Fact]
    public void Only_entities_that_are_not_child_entities_get_a_fingerprint()
    {
        var fingerprints = ComputedFingerprints.Compute(Model());

        Assert.Equal([_orderId], fingerprints.Keys);
        Assert.Matches("^[0-9a-f]{64}$", fingerprints[_orderId]);
    }

    [Fact]
    public void Same_computed_definitions_give_the_same_fingerprint_whatever_else_differs()
    {
        var before = ComputedFingerprints.Compute(Model());
        var after = ComputedFingerprints.Compute(Model(
            orderFields: """
                { "name": "quantity", "type": "integer", "label": { "textKey": "order.quantity" } },
                { "name": "note", "type": "text", "maxLength": 80 },
                { "name": "lines", "type": "child-collection", "target": "Line" },
                { "name": "total", "type": "decimal", "precision": 18, "scale": 2, "label": { "textKey": "order.total" }, "expression": "sum(lines, amount)" }
                """,
            lineFields: """
                { "name": "quantity", "type": "integer", "required": true },
                { "name": "unitPrice", "type": "decimal", "precision": 18, "scale": 2 },
                { "name": "comment", "type": "text" },
                { "name": "amount", "type": "decimal", "precision": 18, "scale": 2, "expression": "quantity * unitPrice" }
                """));

        Assert.Equal(before[_orderId], after[_orderId]);
    }

    [Fact]
    public void An_entity_without_computed_fields_changes_its_fingerprint_when_it_gets_one()
    {
        var plain = ComputedFingerprints.Compute(Model(orderFields: """{ "name": "quantity", "type": "integer" }""", lineFields: """{ "name": "quantity", "type": "integer" }"""));
        var computed = ComputedFingerprints.Compute(Model(orderFields: """
            { "name": "quantity", "type": "integer" },
            { "name": "doubled", "type": "integer", "expression": "quantity * 2" }
            """, lineFields: """{ "name": "quantity", "type": "integer" }"""));

        Assert.NotEqual(plain[_orderId], computed[_orderId]);
    }

    [Fact]
    public void Changing_a_child_computed_expression_changes_the_owner_fingerprint()
    {
        var before = ComputedFingerprints.Compute(Model());
        var after = ComputedFingerprints.Compute(Model(amount: "quantity * unitPrice * 2"));

        Assert.NotEqual(before[_orderId], after[_orderId]);
    }

    [Fact]
    public void Changing_the_owner_computed_expression_or_type_changes_the_owner_fingerprint()
    {
        var before = ComputedFingerprints.Compute(Model());

        Assert.NotEqual(before[_orderId], ComputedFingerprints.Compute(Model(total: "sum(lines, quantity)"))[_orderId]);
        Assert.NotEqual(before[_orderId], ComputedFingerprints.Compute(Model(totalScale: 3))[_orderId]);
    }

    [Fact]
    public void Changing_the_expression_of_a_rule_a_computed_field_calls_changes_the_fingerprint()
    {
        var before = ComputedFingerprints.Compute(WithRule("x * 2"));
        var same = ComputedFingerprints.Compute(WithRule("x * 2"));
        var after = ComputedFingerprints.Compute(WithRule("x * 3"));

        Assert.Equal(before[_orderId], same[_orderId]);
        Assert.NotEqual(before[_orderId], after[_orderId]);
    }

    [Fact]
    public void Changing_the_expression_of_a_rule_a_called_rule_calls_changes_the_fingerprint()
    {
        var before = ComputedFingerprints.Compute(WithRule("x * 2", innerBody: "y + 1"));
        var after = ComputedFingerprints.Compute(WithRule("x * 2", innerBody: "y + 2"));

        Assert.NotEqual(before[_orderId], after[_orderId]);
    }

    private static ApplicationModel Model(
        string? orderFields = null,
        string? lineFields = null,
        string amount = "quantity * unitPrice",
        string total = "sum(lines, amount)",
        int totalScale = 2)
    {
        var compiled = ApplicationCompiler.Compile(
        [
            new ResourceContent("application.json", Manifest),
            new ResourceContent("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "order.quantity": "How many", "order.total": "Total" } }
                """),
            new ResourceContent("entities/order.json", $$"""
                { "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    {{orderFields ?? $$"""
                        { "name": "quantity", "type": "integer" },
                        { "name": "lines", "type": "child-collection", "target": "Line" },
                        { "name": "total", "type": "decimal", "precision": 18, "scale": {{totalScale}}, "expression": "{{total}}" }
                        """}}
                  ] }
                """),
            new ResourceContent("entities/line.json", $$"""
                { "id": "{{_lineId}}", "kind": "entity", "name": "Line", "formatVersion": 1,
                  "fields": [
                    {{lineFields ?? $$"""
                        { "name": "quantity", "type": "integer" },
                        { "name": "unitPrice", "type": "decimal", "precision": 18, "scale": 2 },
                        { "name": "amount", "type": "decimal", "precision": 18, "scale": 2, "expression": "{{amount}}" }
                        """}}
                  ] }
                """),
        ], []);
        Assert.Empty(compiled.Diagnostics);
        return compiled.Model!;
    }

    /// <summary>
    /// A model built by hand, because the compiler does not yet let a computed field call a rule:
    /// <c>Double(quantity)</c> calls a rule whose body is <paramref name="body"/> and which can call
    /// another rule, whose body is <paramref name="innerBody"/>.
    /// </summary>
    private static ApplicationModel WithRule(string body, string innerBody = "y + 1")
    {
        var inner = Rule("Inner", "y", innerBody, []);
        var rule = Rule("Double", "x", body, [inner]);
        var quantity = new FieldModel { Name = "quantity", Type = FieldType.Integer };
        var scope = new ExpressionScope([new KeyValuePair<string, ExpressionType>("quantity", ExpressionType.Integer)], rules: [rule]);
        var doubled = new FieldModel
        {
            Name = "doubled",
            Type = FieldType.Integer,
            Computed = ComputedFieldModel.Compile("Double(quantity)", scope, ExpressionType.Integer),
        };
        var order = new EntityModel { Id = _orderId, Name = "Order", File = "entities/order.json", Fields = [quantity, doubled] };
        return new ApplicationModel
        {
            Manifest = new ApplicationManifest
            {
                Id = Guid.Parse("0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01"),
                Kind = ResourceKinds.Application,
                Name = "Sample",
                FormatVersion = 1,
                File = "application.json",
            },
            Entities = [order],
        };
    }

    /// <summary>
    /// A checked rule with one integer parameter. When <paramref name="called"/> has a rule, the body
    /// is checked with that rule in scope, so the body calls it.
    /// </summary>
    private static ExpressionRule Rule(string name, string parameter, string body, IReadOnlyList<ExpressionRule> called)
    {
        var signature = new ExpressionRule(name, [new ExpressionRuleParameter(parameter, ExpressionType.Integer)], ExpressionType.Integer);
        var text = called.Count == 0 ? body : $"{called[0].Name}({parameter}) + ({body})";
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded);
        var scope = new ExpressionScope(
            [new KeyValuePair<string, ExpressionType>(parameter, ExpressionType.Integer)],
            rules: called);
        var check = ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Integer);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);
        return signature with { Body = parsed.Expression, BodyCheck = check, Expression = text };
    }
}
