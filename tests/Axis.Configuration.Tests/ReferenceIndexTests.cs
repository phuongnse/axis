using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Tests;

public sealed class ReferenceIndexTests
{
    private const string ApplicationWithPackageAndModule = """
        { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1,
          "packages": [ { "id": "test-package", "version": "1.0.0" } ],
          "modules": [ { "id": "departments", "version": "1.0.0" } ] }
        """;

    private const string TextsEn = """
        { "id": "22222222-2222-4222-8222-222222222221", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "order.label": "Order" } }
        """;

    private const string TextsVi = """
        { "id": "22222222-2222-4222-8222-222222222222", "kind": "text", "name": "TextsVi", "formatVersion": 1, "locale": "vi",
          "texts": { "order.label": "Order (vi)" } }
        """;

    [Fact]
    public void A_text_key_used_by_an_entity_label_and_a_field_label_lists_both_and_each_locale_entry()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", Order("order.label", "order.label"))
            .With("texts/en.json", TextsEn)
            .With("texts/vi.json", TextsVi);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [
                ("TextsEn", "/texts/order.label", ReferenceRelations.TextEntry),
                ("TextsVi", "/texts/order.label", ReferenceRelations.TextEntry),
                ("Order", "/label/textKey", ReferenceRelations.LabelText),
                ("Order", "/fields/0/label/textKey", ReferenceRelations.LabelText),
            ],
            result.References.UsedBy(new TextKeyNode("order.label")).Select(edge => (edge.From.Name, edge.Path, edge.Relation)));
    }

    [Fact]
    public void An_application_with_a_pinned_package_and_module_uses_both()
    {
        using var modules = new TemporaryFolder().With("departments/1.0.0/module.json", ModuleCatalogTests.Manifest("departments", "1.0.0"));
        using var folder = new TemporaryFolder().With("application.json", ApplicationWithPackageAndModule);

        var result = ApplicationCompiler.Compile(folder.Path, [new PackageVersion("test-package", "1.0.0")], ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var application = ResourceNode.Of(result.Model!.Manifest);
        Assert.Equal(
            [
                new ReferenceEdge(application, "/packages/0", ReferenceRelations.Package, new PackageNode("test-package")),
                new ReferenceEdge(application, "/modules/0", ReferenceRelations.Module, new ModuleNode("departments")),
            ],
            result.References.Uses(application));
    }

    [Fact]
    public void A_missing_label_key_is_reported_and_adds_no_edge_while_an_existing_key_keeps_its_edge()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", Order("order.label", "order.missing"))
            .With("texts/en.json", TextsEn);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTextKey, "entities/order.json", "/fields/0/label/textKey"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        var labelEdge = Assert.Single(result.References.Edges, edge => edge.Relation == ReferenceRelations.LabelText);
        Assert.Equal(("Order", "/label/textKey"), (labelEdge.From.Name, labelEdge.Path));
        Assert.Equal<ReferenceNode>(new TextKeyNode("order.label"), labelEdge.To);
        Assert.Empty(result.References.UsedBy(new TextKeyNode("order.missing")));
    }

    [Fact]
    public void A_folder_with_errors_still_returns_its_reference_index()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", Order("order.label", "order.missing"))
            .With("texts/en.json", TextsEn);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.True(result.HasErrors);
        Assert.Null(result.Model);
        Assert.Contains(result.References.Edges, edge => edge.Relation == ReferenceRelations.LabelText && edge.From.Name == "Order");
    }

    private const string Texts = """
        { "id": "33333333-3333-4333-8333-333333333399", "kind": "text", "name": "Texts", "formatVersion": 1, "locale": "en",
          "texts": { "order.quantityLarge": "The quantity is large.", "order.submit": "Submit", "orders.title": "Orders",
            "orderForm.title": "Order", "orderLayout.main": "Order", "site.title": "Sales", "nav.orders": "Orders",
            "role.approver": "Approver", "task.review": "Review", "task.ok": "Approve" } }
        """;

    private const string Customer = """
        { "id": "22222222-2222-4222-8222-222222222202", "kind": "entity", "name": "Customer", "formatVersion": 1, "displayField": "name",
          "fields": [ { "name": "name", "type": "text", "required": true }, { "name": "region", "type": "reference", "target": "Region" } ] }
        """;

    private const string Region = """
        { "id": "22222222-2222-4222-8222-222222222203", "kind": "entity", "name": "Region", "formatVersion": 1, "displayField": "name",
          "fields": [ { "name": "name", "type": "text", "required": true } ] }
        """;

    private const string IsLarge = """
        { "id": "66666666-6666-4666-8666-666666666601", "kind": "rule", "name": "IsLarge", "formatVersion": 1,
          "parameters": [{ "name": "value", "type": "integer" }], "resultType": "boolean", "expression": "value > 10" }
        """;

    private const string IsHuge = """
        { "id": "66666666-6666-4666-8666-666666666602", "kind": "rule", "name": "IsHuge", "formatVersion": 1,
          "parameters": [{ "name": "value", "type": "integer" }], "resultType": "boolean", "expression": "IsLarge(value) and value > 100" }
        """;

    private const string OrderNumberSequence = """
        { "id": "77777777-7777-4777-8777-777777777771", "kind": "sequence", "name": "OrderNumber", "formatVersion": 1, "format": "ORD-{yyyy}-{n:5}" }
        """;

    private static readonly ResourceNode _isLargeRule = new(
        Guid.Parse("66666666-6666-4666-8666-666666666601"), ResourceKinds.Rule, "IsLarge", "rules/is-large.json");

    [Fact]
    public void A_rule_called_from_a_validation_a_data_source_filter_and_another_rule_lists_each_caller()
    {
        using var folder = SalesFolder("IsLarge(quantity)")
            .With("rules/is-huge.json", IsHuge)
            .With("data-sources/large-orders.json", DataSource("LargeOrders", "IsLarge(quantity)"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [
                ("IsHuge", "/expression", ReferenceRelations.RuleCall),
                ("Order", "/validations/0/expression", ReferenceRelations.RuleCall),
                ("LargeOrders", "/filter", ReferenceRelations.RuleCall),
            ],
            result.References.UsedBy(_isLargeRule).Select(edge => (edge.From.Name, edge.Path, edge.Relation)));
    }

    [Fact]
    public void A_rule_that_another_rule_calls_is_listed_only_under_that_rule()
    {
        using var folder = SalesFolder("IsHuge(quantity)").With("rules/is-huge.json", IsHuge);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal([("IsHuge", "/expression")], result.References.UsedBy(_isLargeRule).Select(edge => (edge.From.Name, edge.Path)));
    }

    [Fact]
    public void Two_calls_to_one_rule_in_one_expression_give_one_edge()
    {
        using var folder = SalesFolder("IsLarge(quantity) or IsLarge(quantity + 1)");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var edge = Assert.Single(result.References.UsedBy(_isLargeRule));
        Assert.Equal(("Order", "/validations/0/expression"), (edge.From.Name, edge.Path));
    }

    [Fact]
    public void A_data_source_path_through_two_reference_fields_adds_a_field_path_edge_to_each_target()
    {
        using var folder = SalesFolder("IsLarge(quantity)")
            .With("data-sources/order-regions.json", DataSource("OrderRegions", fields: """[ { "name": "regionName", "path": "customer.region.name" } ]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [
                ("OrderRegions", "/fields/0/path", "Customer"),
                ("OrderRegions", "/fields/0/path", "Region"),
            ],
            FieldPathEdges(result));
    }

    [Fact]
    public void A_data_source_path_that_ends_at_a_reference_field_adds_an_edge_to_its_target()
    {
        using var folder = SalesFolder("IsLarge(quantity)")
            .With("data-sources/order-regions.json", DataSource("OrderRegions", fields: """[ { "name": "region", "path": "customer.region" } ]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [
                ("OrderRegions", "/fields/0/path", "Customer"),
                ("OrderRegions", "/fields/0/path", "Region"),
            ],
            FieldPathEdges(result));
    }

    [Fact]
    public void A_page_with_an_unknown_form_adds_no_form_edge_and_another_page_keeps_its_data_source_edge()
    {
        using var folder = SalesFolder("IsLarge(quantity)")
            .With("data-sources/large-orders.json", DataSource("LargeOrders", "IsLarge(quantity)"))
            .With("pages/order-list.json", PresentationCompilerTests.Page(
                "OrderList",
                """{ "type": "table", "dataSource": "LargeOrders" }""",
                "55555555-5555-4555-8555-555555555553"))
            .With("pages/order-form.json", PresentationCompilerTests.Page(
                "OrderForm",
                """{ "type": "form", "form": "Missing" }""",
                "55555555-5555-4555-8555-555555555552",
                "orderForm.title"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownWidgetForm, "pages/order-form.json", "/widgets/0/form"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(
            [(ReferenceRelations.WidgetDataSource, "/widgets/0/dataSource")],
            result.References.Edges
                .Where(edge => edge.From.Name == "OrderList" && edge.Relation != ReferenceRelations.LabelText)
                .Select(edge => (edge.Relation, edge.Path)));
        Assert.DoesNotContain(result.References.Edges, edge => edge.From.Name == "OrderForm" && edge.Relation == ReferenceRelations.WidgetForm);
    }

    [Fact]
    public void A_clean_process_adds_an_edge_for_each_resource_and_package_it_uses()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = ProcessFolder();

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        Assert.Empty(result.Diagnostics);
        Assert.Equivalent(
            new (string, string, string)[]
            {
                (ReferenceRelations.ProcessEntity, "/entity", "Order"),
                (ReferenceRelations.ConnectorCall, "/steps/2/connector/name", "PurchasingSystem"),
                (ReferenceRelations.PackageOperation, "/steps/3/operation", "test-package"),
                (ReferenceRelations.RaisedEvent, "/steps/4/event", "OrderChecked"),
                (ReferenceRelations.TaskRole, "/steps/5/assignee/role", "approver"),
                (ReferenceRelations.TaskForm, "/steps/5/form", "OrderEntry"),
                (ReferenceRelations.TaskForm, "/steps/6/form", "OrderEntry"),
            },
            ProcessEdges(result).Where(edge => edge.Relation != ReferenceRelations.RuleCall && edge.Relation != ReferenceRelations.LabelText));
    }

    [Fact]
    public void A_clean_process_adds_a_rule_call_edge_at_each_expression_path()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = ProcessFolder();

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        Assert.Empty(result.Diagnostics);
        Assert.Equivalent(
            new (string, string, string)[]
            {
                (ReferenceRelations.RuleCall, "/startCondition/expression", "IsLarge"),
                (ReferenceRelations.RuleCall, "/steps/0/branches/0/when", "IsLarge"),
                (ReferenceRelations.RuleCall, "/steps/1/set/quantity", "Doubled"),
                (ReferenceRelations.RuleCall, "/steps/2/input/count", "Doubled"),
                (ReferenceRelations.RuleCall, "/steps/3/input/a", "Doubled"),
                (ReferenceRelations.RuleCall, "/steps/4/fields/count", "Doubled"),
                (ReferenceRelations.RuleCall, "/steps/6/assignee/user", "Assignee"),
            },
            ProcessEdges(result).Where(edge => edge.Relation == ReferenceRelations.RuleCall));
    }

    [Fact]
    public void A_rule_called_in_a_branch_when_and_in_a_grant_filter_lists_both_callers()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = ProcessFolder();

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        Assert.Empty(result.Diagnostics);
        var callers = result.References.UsedBy(_isLargeRule).Select(edge => (edge.From.Name, edge.Path)).ToList();
        Assert.Contains(("OrderReview", "/steps/0/branches/0/when"), callers);
        Assert.Contains(("OrderAccess", "/grants/0/filter"), callers);
    }

    [Fact]
    public void A_policy_adds_a_grant_edge_for_each_target_kind_and_a_role_edge_for_each_role()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = ProcessFolder();

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        Assert.Empty(result.Diagnostics);
        Assert.Equivalent(
            new (string, string, string)[]
            {
                (ReferenceRelations.PolicyRole, "/roles/0", "approver"),
                (ReferenceRelations.Grant, "/grants/0/entity", "Order"),
                (ReferenceRelations.RuleCall, "/grants/0/filter", "IsLarge"),
                (ReferenceRelations.Grant, "/grants/1/dataSource", "LargeOrders"),
                (ReferenceRelations.Grant, "/grants/2/process", "OrderReview"),
                (ReferenceRelations.Grant, "/grants/3/endpoint", "ImportOrder"),
            },
            PolicyEdges(result));
    }

    [Fact]
    public void A_policy_with_an_unknown_role_keeps_its_grant_edge_and_adds_no_role_edge()
    {
        using var folder = SalesFolder("IsLarge(quantity)")
            .With("policies/order-access.json", Policy("""["nobody"]""", """[{ "entity": "Order", "actions": ["read"] }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownPolicyRole, "policies/order-access.json", "/roles/0"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(
            [(ReferenceRelations.Grant, "/grants/0/entity", "Order")],
            PolicyEdges(result));
    }

    [Fact]
    public void A_process_with_an_unknown_form_adds_no_form_edge_and_keeps_its_entity_edge()
    {
        using var folder = SalesFolder("IsLarge(quantity)")
            .With("processes/order-review.json", """
                { "id": "44444444-4444-4444-8444-444444444441", "kind": "process", "name": "OrderReview", "formatVersion": 1,
                  "entity": "Order", "start": "review",
                  "steps": [
                    { "name": "review", "type": "task", "label": { "textKey": "task.review" }, "assignee": { "user": "'anna'" }, "form": "Missing",
                      "outcomes": [ { "name": "ok", "label": { "textKey": "task.ok" }, "next": "done" } ] },
                    { "name": "done", "type": "end" } ] }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownTaskForm, "processes/order-review.json", "/steps/0/form"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(
            [(ReferenceRelations.ProcessEntity, "/entity", "Order")],
            ProcessEdges(result));
    }

    [Fact]
    public void A_clean_folder_produces_an_edge_for_each_relation_this_index_builds()
    {
        using var folder = SalesFolder("IsHuge(quantity)")
            .With("rules/is-huge.json", IsHuge)
            .With("data-sources/large-orders.json", DataSource("LargeOrders", "IsLarge(quantity)"))
            .With("data-sources/order-customers.json", DataSource("OrderCustomers", fields: """[ { "name": "customerName", "path": "customer.name" } ]"""))
            .With("seeds/customers.json", """
                { "id": "99999999-9999-4999-8999-999999999991", "kind": "seed", "name": "Customers", "formatVersion": 1, "entity": "Customer",
                  "records": [ { "id": "99999999-9999-4999-8999-999999999992", "values": { "name": "Acme" } } ] }
                """)
            .With("forms/order-entry.json", """
                { "id": "88888888-8888-4888-8888-888888888881", "kind": "form", "name": "OrderEntry", "formatVersion": 1, "entity": "Order",
                  "sections": [ { "title": { "textKey": "orderLayout.main" }, "fields": [ { "field": "number" }, { "field": "quantity" } ] } ] }
                """)
            .With("processes/order-review.json", """
                { "id": "44444444-4444-4444-8444-444444444441", "kind": "process", "name": "OrderReview", "formatVersion": 1,
                  "entity": "Order", "start": "done", "steps": [ { "name": "done", "type": "end" } ] }
                """)
            .With("pages/orders.json", PresentationCompilerTests.Page(
                "Orders",
                """{ "type": "table", "entity": "Order", "formPage": "OrderForm" }""",
                "55555555-5555-4555-8555-555555555551"))
            .With("pages/order-list.json", PresentationCompilerTests.Page(
                "OrderList",
                """{ "type": "table", "dataSource": "LargeOrders" }""",
                "55555555-5555-4555-8555-555555555553"))
            .With("pages/order-review.json", PresentationCompilerTests.Page(
                "OrderReviewPage",
                """{ "type": "processInspector", "process": "OrderReview" }""",
                "55555555-5555-4555-8555-555555555554"))
            .With("pages/order-form.json", PresentationCompilerTests.Page(
                "OrderForm",
                """{ "type": "form", "form": "OrderEntry", "actions": [ { "type": "startProcess", "process": "OrderReview", "label": { "textKey": "order.submit" } } ] }""",
                "55555555-5555-4555-8555-555555555552",
                "orderForm.title"))
            .With("sites/sales.json", """
                { "id": "66666666-6666-4666-8666-666666666661", "kind": "site", "name": "Sales", "formatVersion": 1, "path": "sales",
                  "title": { "textKey": "site.title" }, "locales": { "default": "en", "fallback": "en", "available": ["en"] },
                  "navigation": [ { "page": "Orders", "label": { "textKey": "nav.orders" } } ] }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var relations = result.References.Edges.Select(edge => edge.Relation).ToHashSet();
        Assert.All(_expectedRelations, relation => Assert.Contains(relation, relations));
    }

    private static readonly string[] _expectedRelations =
    [
        ReferenceRelations.FieldTarget,
        ReferenceRelations.FieldSequence,
        ReferenceRelations.RuleCall,
        ReferenceRelations.NavigationPage,
        ReferenceRelations.WidgetEntity,
        ReferenceRelations.WidgetDataSource,
        ReferenceRelations.WidgetForm,
        ReferenceRelations.WidgetFormPage,
        ReferenceRelations.WidgetProcess,
        ReferenceRelations.ActionProcess,
        ReferenceRelations.FormEntity,
        ReferenceRelations.SeedEntity,
        ReferenceRelations.DataSourceEntity,
        ReferenceRelations.FieldPath,
    ];

    private static List<(string From, string Path, string To)> FieldPathEdges(CompilationResult result) =>
        result.References.Edges
            .Where(edge => edge.Relation == ReferenceRelations.FieldPath)
            .Select(edge => (edge.From.Name, edge.Path, ((ResourceNode)edge.To).Name))
            .ToList();

    /// <summary>
    /// An Order with a sequence number, a customer and a validation calling <c>IsLarge</c> with
    /// <paramref name="validation"/>, beside its Customer and Region, the rule <c>IsLarge</c> and the texts.
    /// </summary>
    private static TemporaryFolder SalesFolder(string validation) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", Texts)
            .With("entities/order.json", $$"""
                { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "number", "type": "text", "sequence": "OrderNumber" },
                    { "name": "quantity", "type": "integer" },
                    { "name": "customer", "type": "reference", "target": "Customer" } ],
                  "validations": [ { "expression": "{{validation}}", "message": { "textKey": "order.quantityLarge" }, "field": "quantity" } ] }
                """)
            .With("entities/customer.json", Customer)
            .With("entities/region.json", Region)
            .With("rules/is-large.json", IsLarge)
            .With("sequences/order-number.json", OrderNumberSequence);

    private static string DataSource(string name, string? filter = null, string fields = """[ { "name": "quantity", "path": "quantity" } ]""")
    {
        var filterProperty = filter is null ? "" : $""", "filter": "{filter}" """;
        return $$"""
            { "id": "{{Guid.NewGuid()}}", "kind": "dataSource", "name": "{{name}}", "formatVersion": 1, "entity": "Order", "fields": {{fields}}{{filterProperty}} }
            """;
    }

    private const string Doubled = """
        { "id": "66666666-6666-4666-8666-666666666603", "kind": "rule", "name": "Doubled", "formatVersion": 1,
          "parameters": [{ "name": "value", "type": "integer" }], "resultType": "integer", "expression": "value * 2" }
        """;

    private const string Assignee = """
        { "id": "66666666-6666-4666-8666-666666666604", "kind": "rule", "name": "Assignee", "formatVersion": 1,
          "parameters": [{ "name": "value", "type": "integer" }], "resultType": "text", "expression": "'anna'" }
        """;

    private static List<(string Relation, string Path, string To)> ProcessEdges(CompilationResult result) =>
        EdgesFrom(result, "OrderReview");

    private static List<(string Relation, string Path, string To)> PolicyEdges(CompilationResult result) =>
        EdgesFrom(result, "OrderAccess");

    /// <summary>The edges of the named resource, except those to text keys, as relation, path and the name of what they reach.</summary>
    private static List<(string Relation, string Path, string To)> EdgesFrom(CompilationResult result, string from) =>
        result.References.Edges
            .Where(edge => edge.From.Name == from && edge.To is not TextKeyNode)
            .Select(edge => (edge.Relation, edge.Path, edge.To switch
            {
                ResourceNode resource => resource.Name,
                PackageNode package => package.Id,
                _ => throw new InvalidOperationException($"Unexpected node {edge.To}."),
            }))
            .ToList();

    private static string Policy(string roles, string grants) => $$"""
        { "id": "12121212-1212-4212-8212-121212121201", "kind": "policy", "name": "OrderAccess", "formatVersion": 1, "roles": {{roles}}, "grants": {{grants}} }
        """;

    /// <summary>
    /// The sales folder with a process that uses an entity, a connector, a package, an event, a role
    /// and a form and calls rules at every expression path, and a policy with a grant of each target kind.
    /// </summary>
    private static TemporaryFolder ProcessFolder() =>
        SalesFolder("IsLarge(quantity)")
            .With("application.json", """
                { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "packages": [{ "id": "test-package", "version": "^1.0.0" }] }
                """)
            .With("rules/doubled.json", Doubled)
            .With("rules/assignee.json", Assignee)
            .With("roles/approver.json", """
                { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "approver", "formatVersion": 1, "label": { "textKey": "role.approver" } }
                """)
            .With("connectors/purchasing-system.json", """
                { "id": "55555555-5555-4555-8555-555555555501", "kind": "connector", "name": "PurchasingSystem", "formatVersion": 1,
                  "baseUrl": { "binding": "PurchasingSystemUrl" }, "auth": { "type": "none" },
                  "operations": [ { "name": "count", "method": "POST", "path": "/count", "input": [ { "name": "count", "type": "integer", "required": true } ] } ] }
                """)
            .With("events/order-checked.json", """
                { "id": "5c1e7a2b-3d4f-4a6b-8c9d-0e1f2a3b4c5d", "kind": "event", "name": "OrderChecked", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "count", "type": "integer" } ] }
                """)
            .With("forms/order-entry.json", """
                { "id": "88888888-8888-4888-8888-888888888881", "kind": "form", "name": "OrderEntry", "formatVersion": 1, "entity": "Order",
                  "sections": [ { "title": { "textKey": "orderLayout.main" }, "fields": [ { "field": "quantity" } ] } ] }
                """)
            .With("data-sources/large-orders.json", DataSource("LargeOrders", "IsLarge(quantity)"))
            .With("processes/order-review.json", """
                { "id": "44444444-4444-4444-8444-444444444441", "kind": "process", "name": "OrderReview", "formatVersion": 1,
                  "entity": "Order", "start": "check",
                  "startCondition": { "expression": "IsLarge(quantity)", "message": { "textKey": "order.quantityLarge" } },
                  "steps": [
                    { "name": "check", "type": "decision", "branches": [ { "when": "IsLarge(quantity)", "next": "update" } ], "otherwise": "update" },
                    { "name": "update", "type": "operation", "operation": "updateRecord", "set": { "quantity": "Doubled(quantity)" }, "next": "connect" },
                    { "name": "connect", "type": "operation", "operation": "callConnector",
                      "connector": { "name": "PurchasingSystem", "operation": "count" }, "input": { "count": "Doubled(quantity)" }, "next": "add" },
                    { "name": "add", "type": "operation", "operation": "test-package/sum", "input": { "a": "Doubled(quantity)", "b": "2" }, "next": "raise" },
                    { "name": "raise", "type": "operation", "operation": "raiseEvent", "event": "OrderChecked",
                      "fields": { "count": "Doubled(quantity)" }, "next": "approve" },
                    { "name": "approve", "type": "task", "label": { "textKey": "task.review" }, "assignee": { "role": "approver" }, "form": "OrderEntry",
                      "outcomes": [ { "name": "ok", "label": { "textKey": "task.ok" }, "next": "confirm" } ] },
                    { "name": "confirm", "type": "task", "label": { "textKey": "task.review" }, "assignee": { "user": "Assignee(quantity)" }, "form": "OrderEntry",
                      "outcomes": [ { "name": "ok", "label": { "textKey": "task.ok" }, "next": "done" } ] },
                    { "name": "done", "type": "end" } ] }
                """)
            .With("endpoints/import-order.json", """
                { "id": "99999999-9999-4999-8999-999999999901", "kind": "endpoint", "name": "ImportOrder", "formatVersion": 1,
                  "entity": "Order", "process": "OrderReview", "input": [], "values": {} }
                """)
            .With("policies/order-access.json", Policy(
                """["approver"]""",
                """
                [
                  { "entity": "Order", "actions": ["read"], "filter": "IsLarge(quantity)" },
                  { "dataSource": "LargeOrders", "actions": ["read"] },
                  { "process": "OrderReview", "actions": ["start"] },
                  { "endpoint": "ImportOrder", "actions": ["invoke"] }
                ]
                """));

    private static string Order(string entityLabelKey, string fieldLabelKey) => $$"""
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1,
          "label": { "textKey": "{{entityLabelKey}}" },
          "fields": [ { "name": "number", "type": "text", "required": true, "label": { "textKey": "{{fieldLabelKey}}" } } ] }
        """;
}
