using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Policy;

namespace Axis.Configuration.Tests;

public sealed class PolicyMatrixTests
{
    private const string RequestId = "11111111-1111-4111-8111-111111111101";
    private const string DataSourceId = "99999999-9999-4999-8999-999999999901";
    private const string ProcessId = "77777777-7777-4777-8777-777777777701";
    private const string EndpointId = "bc241668-fe85-45a4-b7cf-d36da8d56b71";

    private const string Texts = """
        { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "role.employee": "Employee", "role.manager": "Manager" } }
        """;

    [Theory]
    [InlineData("purchase-requests")]
    [InlineData("leave-requests")]
    [InlineData("service-desk")]
    public void Every_cell_of_a_sample_equals_what_the_policy_evaluator_grants(string sample)
    {
        var result = ApplicationCompiler.Compile(
            Path.Combine(AppContext.BaseDirectory, "Samples", sample), SamplePackages.Versions, SampleModules.Modules);

        Assert.Empty(result.Diagnostics);
        var model = result.Model!;
        var matrix = result.PolicyMatrix;

        var rootEntities = model.Entities.Where(entity => !model.IsChildEntity(entity)).Select(entity => entity.Name);
        Assert.Equal(
            rootEntities.Order(StringComparer.Ordinal),
            matrix.Targets.Where(target => target.Kind == "entity").Select(target => target.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            model.DataSources.Select(dataSource => dataSource.Name).Order(StringComparer.Ordinal),
            matrix.Targets.Where(target => target.Kind == "dataSource").Select(target => target.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            model.Processes.Select(process => process.Name).Order(StringComparer.Ordinal),
            matrix.Targets.Where(target => target.Kind == "process").Select(target => target.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            model.Endpoints.Select(endpoint => endpoint.Name).Order(StringComparer.Ordinal),
            matrix.Targets.Where(target => target.Kind == "endpoint").Select(target => target.Name).Order(StringComparer.Ordinal));
        Assert.Equal(model.Roles.Select(role => role.Name), matrix.Roles.Select(role => role.Name));

        foreach (var role in model.Roles)
        {
            var row = matrix.Roles.Single(candidate => candidate.Name == role.Name);
            Assert.Equal(matrix.Targets.Count, row.Cells.Count);
            string[] held = [role.Name];

            foreach (var entity in model.Entities.Where(entity => !model.IsChildEntity(entity)))
            {
                foreach (var action in new[] { PolicyAction.Read, PolicyAction.Create, PolicyAction.Update, PolicyAction.Delete })
                {
                    Assert.Equal(
                        PolicyEvaluator.Access(model, held, entity, action).Granted,
                        Cell(row, "entity", entity.Name).Actions.Contains(Name(action)));
                }

                var entityCell = Cell(row, "entity", entity.Name);
                Assert.Equal(PolicyEvaluator.AllowsExport(model, held, entity), entityCell.Actions.Contains("export"));
                Assert.Equal(PolicyEvaluator.AllowsImport(model, held, entity), entityCell.Actions.Contains("import"));
            }

            foreach (var dataSource in model.DataSources)
            {
                Assert.Equal(
                    PolicyEvaluator.AllowsRead(model, held, dataSource),
                    Cell(row, "dataSource", dataSource.Name).Actions.Contains("read"));
                Assert.Equal(
                    PolicyEvaluator.AllowsExport(model, held, dataSource),
                    Cell(row, "dataSource", dataSource.Name).Actions.Contains("export"));
            }

            foreach (var process in model.Processes)
            {
                var cell = Cell(row, "process", process.Name);
                Assert.Equal(PolicyEvaluator.AllowsStart(model, held, process), cell.Actions.Contains("start"));
                Assert.Equal(PolicyEvaluator.AllowsInspect(model, held, process), cell.Actions.Contains("inspect"));
                Assert.Equal(PolicyEvaluator.AllowsCancel(model, held, process), cell.Actions.Contains("cancel"));
                Assert.Equal(PolicyEvaluator.AllowsRetry(model, held, process), cell.Actions.Contains("retry"));
                Assert.Equal(PolicyEvaluator.AllowsResolve(model, held, process), cell.Actions.Contains("resolve"));
                Assert.Equal(PolicyEvaluator.AllowsReassign(model, held, process), cell.Actions.Contains("reassign"));
            }

            foreach (var endpoint in model.Endpoints)
            {
                Assert.Equal(PolicyEvaluator.AllowsInvoke(model, held, endpoint), Cell(row, "endpoint", endpoint.Name).Actions.Contains("invoke"));
            }
        }
    }

    [Fact]
    public void A_grant_from_a_policy_naming_two_roles_is_shared_and_one_from_the_roles_own_access_policy_is_not()
    {
        using var folder = Folder(
            ("policies/shared.json", Policy("11", "RequestAccess", """["employee", "manager"]""", """[{ "entity": "Request", "actions": ["read"] }]""")),
            ("policies/employee.json", Policy("12", "EmployeeAccess", """["employee"]""", """[{ "entity": "Request", "actions": ["create"], "filter": "owner == 'x'", "fields": ["owner"] }]""")));

        var matrix = ApplicationCompiler.Compile(folder.Path).PolicyMatrix;

        var employee = Role(matrix, "employee");
        var cell = Cell(employee, "entity", "Request");
        Assert.Equal(["read", "create"], cell.Actions);
        var shared = Assert.Single(cell.Grants, grant => grant.Policy == "RequestAccess");
        var own = Assert.Single(cell.Grants, grant => grant.Policy == "EmployeeAccess");
        Assert.True(shared.Shared);
        Assert.False(own.Shared);
        Assert.Equal((0, "owner == 'x'"), (own.Index, own.Filter));
        Assert.Equal(["owner"], own.Fields);
        Assert.Equal(Guid.Parse(Id("12")), own.PolicyId);
        Assert.True(employee.Editable);
        Assert.Null(employee.ReadOnlyReason);
        Assert.Equal(new PolicyMatrixAccessPolicy("EmployeeAccess", Guid.Parse(Id("12"))), employee.AccessPolicy);

        var manager = Role(matrix, "manager");
        Assert.Equal(["read"], Cell(manager, "entity", "Request").Actions);
        Assert.True(Assert.Single(Cell(manager, "entity", "Request").Grants).Shared);
    }

    [Fact]
    public void An_access_policy_that_names_another_role_makes_the_row_read_only_and_a_missing_one_leaves_it_editable()
    {
        using var folder = Folder(
            ("policies/employee.json", Policy("12", "EmployeeAccess", """["manager"]""", """[{ "entity": "Request", "actions": ["read"] }]""")));

        var matrix = ApplicationCompiler.Compile(folder.Path).PolicyMatrix;

        var employee = Role(matrix, "employee");
        Assert.False(employee.Editable);
        Assert.Equal("accessPolicyShared", employee.ReadOnlyReason);
        Assert.Equal(new PolicyMatrixAccessPolicy("EmployeeAccess", Guid.Parse(Id("12"))), employee.AccessPolicy);
        Assert.Empty(Cell(employee, "entity", "Request").Grants);

        var manager = Role(matrix, "manager");
        Assert.True(manager.Editable);
        Assert.Null(manager.ReadOnlyReason);
        Assert.Equal(new PolicyMatrixAccessPolicy("ManagerAccess", null), manager.AccessPolicy);
        Assert.True(Assert.Single(Cell(manager, "entity", "Request").Grants).Shared);
    }

    [Fact]
    public void A_data_source_read_without_read_on_its_root_entity_gives_no_read_and_names_the_entity()
    {
        using var folder = Folder(
            ("policies/employee.json", Policy("12", "EmployeeAccess", """["employee"]""", """[{ "dataSource": "RequestList", "actions": ["read"] }]""")),
            ("policies/manager.json", Policy("13", "ManagerAccess", """["manager"]""", """
                [{ "dataSource": "RequestList", "actions": ["read"] }, { "entity": "Request", "actions": ["read"] }]
                """)));

        var matrix = ApplicationCompiler.Compile(folder.Path).PolicyMatrix;

        var blocked = Cell(Role(matrix, "employee"), "dataSource", "RequestList");
        Assert.Empty(blocked.Actions);
        Assert.Equal("Request", blocked.MissingEntityRead);
        Assert.Single(blocked.Grants);
        Assert.Equal("Request", Assert.Single(matrix.Targets, target => target.Kind == "dataSource").Entity);

        var allowed = Cell(Role(matrix, "manager"), "dataSource", "RequestList");
        Assert.Equal(["read"], allowed.Actions);
        Assert.Null(allowed.MissingEntityRead);
    }

    [Fact]
    public void An_export_or_import_shows_only_when_the_role_also_holds_read_or_create_and_a_data_source_export_needs_the_entity_read()
    {
        using var folder = Folder(
            ("policies/employee.json", Policy("12", "EmployeeAccess", """["employee"]""", """
                [
                  { "dataSource": "RequestList", "actions": ["read", "export"] },
                  { "entity": "Request", "actions": ["export", "import"] }
                ]
                """)),
            ("policies/manager.json", Policy("13", "ManagerAccess", """["manager"]""", """
                [
                  { "dataSource": "RequestList", "actions": ["read", "export"] },
                  { "entity": "Request", "actions": ["read", "export", "import"] }
                ]
                """)));

        var matrix = ApplicationCompiler.Compile(folder.Path).PolicyMatrix;

        // The employee cannot read the entity, so the data source shows neither read nor export,
        // and the entity shows neither export nor import.
        var employee = Role(matrix, "employee");
        var blocked = Cell(employee, "dataSource", "RequestList");
        Assert.Empty(blocked.Actions);
        Assert.Equal("Request", blocked.MissingEntityRead);
        Assert.Empty(Cell(employee, "entity", "Request").Actions);

        // The manager holds read, so the export shows, but an import without create does not.
        var manager = Role(matrix, "manager");
        Assert.Equal(["read", "export"], Cell(manager, "dataSource", "RequestList").Actions);
        Assert.Equal(["read", "export"], Cell(manager, "entity", "Request").Actions);
    }

    [Fact]
    public void Each_kind_takes_the_actions_of_policy_schema_json_and_the_flat_list_is_their_union()
    {
        string[] kinds = ["entity", "dataSource", "process", "endpoint"];

        Assert.Equal(["read", "create", "update", "delete", "export", "import"], PolicyActions.Of("entity"));
        Assert.Equal(["read", "export"], PolicyActions.Of("dataSource"));
        Assert.Equal(
            PolicyActions.All.Order(StringComparer.Ordinal),
            kinds.SelectMany(PolicyActions.Of).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Columns_list_root_entities_then_data_sources_processes_and_endpoints_and_leave_child_entities_out()
    {
        using var folder = Folder(
            ("policies/employee.json", Policy("12", "EmployeeAccess", """["employee"]""", """
                [{ "entity": "RequestLine", "actions": ["read"] }, { "process": "requestreview", "actions": ["start", "read"] }]
                """)));

        var matrix = ApplicationCompiler.Compile(folder.Path).PolicyMatrix;

        Assert.Equal(
            [("entity", "Department"), ("entity", "Request"), ("dataSource", "RequestList"), ("process", "RequestReview"), ("endpoint", "SubmitRequest")],
            matrix.Targets.Select(target => (target.Kind, target.Name)).OrderBy(target => Array.IndexOf(["entity", "dataSource", "process", "endpoint"], target.Kind)).ThenBy(target => target.Name, StringComparer.Ordinal));
        Assert.Equal(["start", "inspect", "cancel", "retry", "resolve", "reassign"], Assert.Single(matrix.Targets, target => target.Kind == "process").Actions);

        // A grant on a child entity has no column, and an action the column does not accept is left out of the cell.
        var employee = Role(matrix, "employee");
        Assert.Equal(matrix.Targets.Count, employee.Cells.Count);
        var process = Cell(employee, "process", "RequestReview");
        Assert.Equal(["start"], process.Actions);
        Assert.Equal(["start", "read"], Assert.Single(process.Grants).Actions);
    }

    [Fact]
    public void A_folder_with_a_compile_error_still_gets_its_roles_and_grants()
    {
        using var folder = Folder(
            ("policies/employee.json", Policy("12", "EmployeeAccess", """["employee"]""", """[{ "entity": "Request", "actions": ["read"] }]""")))
            .With("endpoints/broken.json", $$"""
                { "id": "bc241668-fe85-45a4-b7cf-d36da8d56b72", "kind": "endpoint", "name": "Broken", "formatVersion": 1, "entity": "Request",
                  "process": "NoSuchProcess", "input": [], "values": {} }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.True(result.HasErrors);
        Assert.Null(result.Model);
        Assert.Equal(["employee", "manager"], result.PolicyMatrix.Roles.Select(role => role.Name).Order(StringComparer.Ordinal));
        var cell = Cell(Role(result.PolicyMatrix, "employee"), "entity", "Request");
        Assert.Equal(["read"], cell.Actions);
        Assert.Single(cell.Grants);
    }

    [Fact]
    public void In_the_service_desk_only_the_requester_may_delete_a_ticket()
    {
        var result = ApplicationCompiler.Compile(
            Path.Combine(AppContext.BaseDirectory, "Samples", "service-desk"), SamplePackages.Versions, SampleModules.Modules);

        Assert.Empty(result.Diagnostics);
        var matrix = result.PolicyMatrix;

        var requester = Cell(Role(matrix, "requester"), "entity", "Ticket");
        Assert.Contains("delete", requester.Actions);
        Assert.Contains(requester.Grants, grant => grant.Filter == "requester == currentUser() and status == 'draft'");
        Assert.DoesNotContain("delete", Cell(Role(matrix, "agent"), "entity", "Ticket").Actions);
        Assert.DoesNotContain("delete", Cell(Role(matrix, "service-desk-lead"), "entity", "Ticket").Actions);
    }

    private static string Name(PolicyAction action) => action.ToString().ToLowerInvariant();

    private static PolicyMatrixRole Role(PolicyMatrix matrix, string name) => Assert.Single(matrix.Roles, role => role.Name == name);

    private static PolicyMatrixCell Cell(PolicyMatrixRole row, string kind, string target) =>
        Assert.Single(row.Cells, cell => cell.Kind == kind && cell.Target == target);

    private static string Id(string suffix) => $"12121212-1212-4212-8212-1212121212{suffix}";

    private static string Policy(string idSuffix, string name, string roles, string grants) => $$"""
        { "id": "{{Id(idSuffix)}}", "kind": "policy", "name": "{{name}}", "formatVersion": 1, "roles": {{roles}}, "grants": {{grants}} }
        """;

    /// <summary>
    /// An application with the roles <c>employee</c> and <c>manager</c>, a root entity <c>Request</c> that
    /// references a <c>Department</c> and owns <c>RequestLine</c> rows, the data source <c>RequestList</c>,
    /// the process <c>RequestReview</c>, the endpoint <c>SubmitRequest</c>, and the given policy files.
    /// </summary>
    private static TemporaryFolder Folder(params (string File, string Content)[] policies)
    {
        var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", Texts)
            .With("roles/employee.json", """
                { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "employee", "formatVersion": 1, "label": { "textKey": "role.employee" } }
                """)
            .With("roles/manager.json", """
                { "id": "44444444-4444-4444-8444-444444444402", "kind": "role", "name": "manager", "formatVersion": 1, "label": { "textKey": "role.manager" } }
                """)
            .With("entities/request.json", $$"""
                { "id": "{{RequestId}}", "kind": "entity", "name": "Request", "formatVersion": 1,
                  "fields": [
                    { "name": "owner", "type": "text" },
                    { "name": "department", "type": "reference", "target": "Department" },
                    { "name": "lines", "type": "child-collection", "target": "RequestLine" }
                  ] }
                """)
            .With("entities/department.json", """
                { "id": "11111111-1111-4111-8111-111111111102", "kind": "entity", "name": "Department", "formatVersion": 1,
                  "displayField": "name", "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """)
            .With("entities/request-line.json", """
                { "id": "11111111-1111-4111-8111-111111111103", "kind": "entity", "name": "RequestLine", "formatVersion": 1,
                  "fields": [ { "name": "quantity", "type": "integer" } ] }
                """)
            .With("data-sources/request-list.json", $$"""
                { "id": "{{DataSourceId}}", "kind": "dataSource", "name": "RequestList", "formatVersion": 1, "entity": "Request",
                  "fields": [{ "name": "owner", "path": "owner" }] }
                """)
            .With("processes/request-review.json", $$"""
                { "id": "{{ProcessId}}", "kind": "process", "name": "RequestReview", "formatVersion": 1, "entity": "Request",
                  "start": "done", "steps": [{ "name": "done", "type": "end" }] }
                """)
            .With("endpoints/submit-request.json", $$"""
                { "id": "{{EndpointId}}", "kind": "endpoint", "name": "SubmitRequest", "formatVersion": 1, "entity": "Request",
                  "process": "RequestReview", "input": [{ "name": "owner", "type": "text" }], "values": { "owner": "owner" } }
                """);
        foreach (var (file, content) in policies)
        {
            folder.With(file, content);
        }

        return folder;
    }
}
