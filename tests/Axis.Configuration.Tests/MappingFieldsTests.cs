using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Tests;

public sealed class MappingFieldsTests
{
    private static readonly Guid _requestId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid _departmentId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid _personId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid _lineId = Guid.Parse("44444444-4444-4444-8444-444444444444");

    [Fact]
    public void A_reference_path_gives_every_field_along_it()
    {
        var fields = Collect(("reference", "department.manager.name"));

        Assert.Equal(
            [(_requestId, "department"), (_departmentId, "manager"), (_personId, "name")],
            Sorted(fields));
    }

    [Fact]
    public void A_path_in_a_different_letter_case_gives_the_declared_field_names()
    {
        var fields = Collect(("reference", "DEPARTMENT.Name"));

        Assert.Equal([(_requestId, "department"), (_departmentId, "name")], Sorted(fields));
    }

    [Fact]
    public void Every_argument_of_a_rule_call_gives_its_fields_and_the_rule_body_gives_none()
    {
        var fields = Collect(("flag", "Within(amount * 2, department.limit)"));

        Assert.Equal(
            [(_requestId, "amount"), (_requestId, "department"), (_departmentId, "limit")],
            Sorted(fields));
    }

    [Fact]
    public void An_aggregate_gives_the_owners_collection_field_and_not_the_child_fields()
    {
        var fields = Collect(("count", "sum(lines, quantity)"));

        Assert.Equal([(_requestId, "lines")], Sorted(fields));
    }

    [Fact]
    public void A_list_mapping_gives_the_owners_collection_field_and_the_fields_its_items_read_through_paths()
    {
        var fields = Collect(("lines", """{ "from": "lines", "fields": { "count": "quantity", "label": "product.name" } }"""));

        Assert.Equal([(_requestId, "lines"), (_personId, "name")], Sorted(fields));
    }

    [Fact]
    public void An_object_mapping_reads_over_the_subject()
    {
        var fields = Collect(("address", """{ "fields": { "city": "department.name" } }"""));

        Assert.Equal([(_requestId, "department"), (_departmentId, "name")], Sorted(fields));
    }

    [Fact]
    public void A_list_mapping_from_a_name_the_entity_does_not_have_gives_null()
    {
        var model = Model("amount");
        var item = new ConnectorInputModel("lines", null) { From = "gone", Fields = [] };

        Assert.Null(MappingFields.Collect(model, Request(model), [item], []));
        Assert.Null(MappingFields.Collect(model, Request(model), [new ConnectorInputModel("lines", null) { From = "title", Fields = [] }], []));
    }

    [Fact]
    public void Functions_walk_all_their_arguments_and_literals_give_nothing()
    {
        var fields = Collect(("reference", "if(amount is null, concat(title, 'x'), lower(comment))"));

        Assert.Equal([(_requestId, "amount"), (_requestId, "comment"), (_requestId, "title")], Sorted(fields));
    }

    [Fact]
    public void An_output_gives_the_subject_field_it_is_written_to()
    {
        var fields = Collect(("amount", "amount"), outputs: ("orderNumber", "COMMENT"));

        Assert.Equal([(_requestId, "amount"), (_requestId, "comment")], Sorted(fields));
    }

    [Fact]
    public void An_expression_with_a_node_type_the_walker_does_not_know_gives_null()
    {
        var model = Model("amount");
        var strange = new ExpressionModel
        {
            Expression = "x",
            Syntax = new StrangeNode(0),
            Check = new ExpressionCheckResult(ExpressionType.Text, null),
        };

        Assert.Null(MappingFields.Collect(model, Request(model), [new ConnectorInputModel("reference", strange)], []));
        var inside = new ExpressionModel
        {
            Expression = "x",
            Syntax = new BinaryNode(0, BinaryOperator.Add, new IntegerLiteral(0, 1), new StrangeNode(2)),
            Check = new ExpressionCheckResult(ExpressionType.Integer, null),
        };
        Assert.Null(MappingFields.Collect(model, Request(model), [new ConnectorInputModel("count", inside)], []));
    }

    [Fact]
    public void A_path_step_without_a_recorded_target_or_a_call_that_is_no_function_or_rule_gives_null()
    {
        var model = Model("amount");
        var check = new ExpressionCheckResult(ExpressionType.Text, null);
        var path = new ExpressionModel
        {
            Expression = "department.name",
            Syntax = new MemberNode(10, new NameNode(0, "department"), "name"),
            Check = check,
        };
        var call = new ExpressionModel
        {
            Expression = "Mystery(title)",
            Syntax = new CallNode(0, "Mystery", [new NameNode(8, "title")]),
            Check = check,
        };

        Assert.Null(MappingFields.Collect(model, Request(model), [new ConnectorInputModel("reference", path)], []));
        Assert.Null(MappingFields.Collect(model, Request(model), [new ConnectorInputModel("reference", call)], []));
    }

    [Fact]
    public void A_name_or_output_the_entity_does_not_have_gives_null()
    {
        var model = Model("amount");
        var request = Request(model);
        var missing = new ExpressionModel
        {
            Expression = "gone",
            Syntax = new NameNode(0, "gone"),
            Check = new ExpressionCheckResult(ExpressionType.Text, null),
        };

        Assert.Null(MappingFields.Collect(model, request, [new ConnectorInputModel("reference", missing)], []));
        Assert.Null(MappingFields.Collect(model, request, [], [new ConnectorOutputModel("orderNumber", "gone")]));
    }

    private static IReadOnlySet<MappedField> Collect(
        (string Input, string Value) input,
        (string Output, string Field)? outputs = null)
    {
        var model = Model(input.Input, input.Value, outputs);
        var step = Assert.IsType<CallConnectorStepModel>(Assert.Single(Assert.Single(model.Processes).Steps, candidate => candidate is CallConnectorStepModel));
        var fields = MappingFields.Collect(model, Request(model), step.Input, step.Output);
        return Assert.IsAssignableFrom<IReadOnlySet<MappedField>>(fields);
    }

    private static List<(Guid, string)> Sorted(IReadOnlySet<MappedField> fields) =>
        fields.Select(field => (field.EntityId, field.Field)).OrderBy(field => field.EntityId).ThenBy(field => field.Field, StringComparer.Ordinal).ToList();

    private static EntityModel Request(ApplicationModel model) => model.FindEntity(_requestId)!;

    private static ApplicationModel Model(string input, string value = "amount", (string Output, string Field)? output = null)
    {
        // A value that starts with a brace is a nested mapping, and any other is an expression.
        var valueJson = value.StartsWith('{') ? value : $"\"{value}\"";
        var outputJson = output is { } o ? $$"""{ "{{o.Output}}": "{{o.Field}}" }""" : "{}";
        var compiled = ApplicationCompiler.Compile(
        [
            new ResourceContent("application.json", """
                { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }
                """),
            new ResourceContent("entities/request.json", $$"""
                { "id": "{{_requestId}}", "kind": "entity", "name": "Request", "formatVersion": 1,
                  "fields": [
                    { "name": "title", "type": "text" },
                    { "name": "amount", "type": "decimal", "precision": 18, "scale": 2 },
                    { "name": "comment", "type": "text" },
                    { "name": "department", "type": "reference", "target": "Department" },
                    { "name": "lines", "type": "child-collection", "target": "Line" }
                  ] }
                """),
            new ResourceContent("entities/department.json", $$"""
                { "id": "{{_departmentId}}", "kind": "entity", "name": "Department", "formatVersion": 1, "displayField": "name",
                  "fields": [
                    { "name": "name", "type": "text", "required": true },
                    { "name": "limit", "type": "decimal", "precision": 18, "scale": 2 },
                    { "name": "manager", "type": "reference", "target": "Person" }
                  ] }
                """),
            new ResourceContent("entities/person.json", $$"""
                { "id": "{{_personId}}", "kind": "entity", "name": "Person", "formatVersion": 1, "displayField": "name",
                  "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """),
            new ResourceContent("entities/line.json", $$"""
                { "id": "{{_lineId}}", "kind": "entity", "name": "Line", "formatVersion": 1,
                  "fields": [ { "name": "quantity", "type": "integer" }, { "name": "product", "type": "reference", "target": "Person" } ] }
                """),
            new ResourceContent("rules/within.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "rule", "name": "Within", "formatVersion": 1,
                  "parameters": [ { "name": "total", "type": "decimal" }, { "name": "limit", "type": "decimal" } ],
                  "resultType": "boolean", "expression": "total <= limit" }
                """),
            new ResourceContent("connectors/purchasing.json", """
                { "id": "66666666-6666-4666-8666-666666666666", "kind": "connector", "name": "Purchasing", "formatVersion": 1,
                  "baseUrl": { "binding": "PurchasingUrl" },
                  "auth": { "type": "bearer", "token": { "binding": "PurchasingToken" } },
                  "idempotency": "Idempotency-Key",
                  "operations": [
                    { "name": "createOrder", "method": "POST", "path": "/orders",
                      "input": [
                        { "name": "reference", "type": "text" },
                        { "name": "amount", "type": "decimal" },
                        { "name": "flag", "type": "boolean" },
                        { "name": "count", "type": "integer" },
                        { "name": "lines", "type": "list", "fields": [ { "name": "count", "type": "integer" }, { "name": "label", "type": "text" } ] },
                        { "name": "address", "type": "object", "fields": [ { "name": "city", "type": "text" } ] }
                      ],
                      "output": [ { "name": "orderNumber", "type": "text" } ] }
                  ] }
                """),
            new ResourceContent("processes/order.json", $$"""
                { "id": "77777777-7777-4777-8777-777777777777", "kind": "process", "name": "Order", "formatVersion": 1,
                  "entity": "Request", "start": "call",
                  "steps": [
                    { "name": "call", "type": "operation", "operation": "callConnector",
                      "connector": { "name": "Purchasing", "operation": "createOrder" },
                      "input": { "{{input}}": {{valueJson}} }, "output": {{outputJson}}, "next": "done" },
                    { "name": "done", "type": "end" }
                  ] }
                """),
        ], []);
        Assert.Empty(compiled.Diagnostics);
        return compiled.Model!;
    }

    private sealed record StrangeNode(int Offset) : ExpressionNode(Offset);
}
