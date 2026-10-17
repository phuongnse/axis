using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Sql;
using Axis.Policy;

namespace Axis.Configuration.Tests;

public sealed class PolicyCompilerTests
{
    private const string PolicyId = "12121212-1212-4212-8212-121212121201";
    private const string RequestId = "11111111-1111-4111-8111-111111111101";
    private const string DataSourceId = "99999999-9999-4999-8999-999999999901";
    private const string ProcessId = "77777777-7777-4777-8777-777777777701";
    private const string EndpointId = "bc241668-fe85-45a4-b7cf-d36da8d56b71";
    private const string PolicyFile = "policies/requests.json";

    private const string Texts = """
        { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": {
            "request.cannotStart": "This request cannot be started.",
            "request.invalid": "The request is not valid.",
            "role.employee": "Employee",
            "role.manager": "Manager"
          } }
        """;

    private const string FullPolicyGrants = """
        [
          { "entity": "request", "actions": ["read", "update"], "filter": "owner == currentUser() and department.name != 'x'", "fields": ["OWNER", "amount"] },
          { "dataSource": "requestList", "actions": ["read"] },
          { "process": "requestReview", "actions": ["start"] }
        ]
        """;

    [Fact]
    public void A_policy_with_entity_data_source_and_process_grants_compiles_into_the_model_and_the_release()
    {
        using var folder = Folder(Policy("""["employee", "manager"]""", FullPolicyGrants));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var policy = Assert.Single(result.Model.Policies);
        Assert.Equal((Guid.Parse(PolicyId), "RequestAccess", PolicyFile), (policy.Id, policy.Name, policy.File));
        Assert.Equal(["employee", "manager"], policy.Roles);
        Assert.Equal(3, policy.Grants.Count);

        // Targets resolve ignoring letter case to the declared names, and fields to the entity's names.
        var entityGrant = policy.Grants[0];
        Assert.Equal(new EntityReference(Guid.Parse(RequestId), "Request"), entityGrant.Entity);
        Assert.Equal((null, null), (entityGrant.DataSource, entityGrant.Process));
        Assert.Equal([PolicyAction.Read, PolicyAction.Update], entityGrant.Actions);
        Assert.Equal(["owner", "amount"], entityGrant.Fields);
        Assert.NotNull(entityGrant.Filter);
        Assert.Equal("owner == currentUser() and department.name != 'x'", entityGrant.Filter.Expression);
        var sql = SqlTranslator.Translate(entityGrant.Filter.Syntax, path => string.Join('.', path), entityGrant.Filter.Check);
        Assert.True(sql.Succeeded, sql.Diagnostic?.Message);
        Assert.Contains("owner IS NOT DISTINCT FROM @currentUser", sql.Sql, StringComparison.Ordinal);

        var dataSourceGrant = policy.Grants[1];
        Assert.Equal(
            (null, new DataSourceReference(Guid.Parse(DataSourceId), "RequestList"), null, null, null),
            (dataSourceGrant.Entity, dataSourceGrant.DataSource, dataSourceGrant.Process, dataSourceGrant.Filter, dataSourceGrant.Fields));
        Assert.Equal([PolicyAction.Read], dataSourceGrant.Actions);
        var processGrant = policy.Grants[2];
        Assert.Equal(
            (null, null, new ProcessReference(Guid.Parse(ProcessId), "RequestReview"), null, null),
            (processGrant.Entity, processGrant.DataSource, processGrant.Process, processGrant.Filter, processGrant.Fields));
        Assert.Equal([PolicyAction.Start], processGrant.Actions);

        Assert.Contains(result.Resources, resource => resource.Path == PolicyFile);
        var rebuilt = ApplicationCompiler.Compile(result.Resources);
        Assert.Empty(rebuilt.Diagnostics);
        Assert.Equal(result.ContentHash, rebuilt.ContentHash);
        Assert.Equal(policy.Grants.Select(grant => grant.Filter?.Expression), Assert.Single(rebuilt.Model!.Policies).Grants.Select(grant => grant.Filter?.Expression));
    }

    [Fact]
    public void A_policy_filter_can_pass_current_user_to_a_rule()
    {
        using var folder = Folder(Policy("""["employee"]""", """[{ "entity": "Request", "actions": ["read"], "filter": "IsOwner(owner, currentUser())" }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var filter = Assert.Single(Assert.Single(result.Model!.Policies).Grants).Filter!;
        var sql = SqlTranslator.Translate(filter.Syntax, path => string.Join('.', path), filter.Check);
        Assert.True(sql.Succeeded, sql.Diagnostic?.Message);
        Assert.Contains("@currentUser", sql.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_roles_targets_and_fields_are_reported_at_their_paths_in_one_pass()
    {
        using var folder = Folder(Policy("""["employee", "Manager", "nobody"]""", """
            [
              { "entity": "Nope", "actions": ["read"] },
              { "dataSource": "Nope", "actions": ["read"] },
              { "process": "Nope", "actions": ["start"] },
              { "entity": "Request", "actions": ["read"], "fields": ["owner", "nope"] },
              { "endpoint": "Nope", "actions": ["invoke"] }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        // Role names are matched exactly, so 'Manager' is not 'manager'.
        Assert.Equal(
            [
                (DiagnosticCodes.UnknownGrantTarget, "/grants/0/entity", "The entity 'Nope' was not found. No loaded entity has that name."),
                (DiagnosticCodes.UnknownGrantTarget, "/grants/1/dataSource", "The data source 'Nope' was not found. No loaded data source has that name."),
                (DiagnosticCodes.UnknownGrantTarget, "/grants/2/process", "The process 'Nope' was not found. No loaded process has that name."),
                (DiagnosticCodes.UnknownGrantField, "/grants/3/fields/1", "The field 'nope' was not found. The entity 'Request' has no field with that name."),
                (DiagnosticCodes.UnknownGrantTarget, "/grants/4/endpoint", "The endpoint 'Nope' was not found. No loaded endpoint has that name."),
                (DiagnosticCodes.UnknownPolicyRole, "/roles/1", "The role 'Manager' was not found. No declared role has that name."),
                (DiagnosticCodes.UnknownPolicyRole, "/roles/2", "The role 'nobody' was not found. No declared role has that name."),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path, diagnostic.Message)));
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((PolicyFile, Guid.Parse(PolicyId)), (diagnostic.File, diagnostic.ResourceId)));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_target_or_role_whose_file_was_not_loaded_is_not_reported_again()
    {
        using var folder = Folder(Policy("""["auditor"]""", """[{ "entity": "Audit", "actions": ["read"] }]"""))
            .With("roles/auditor.json", """{ "id": "44444444-4444-4444-8444-444444444409", "kind": "role", "name": "auditor", "formatVersion": 1 }""")
            .With("entities/audit.json", """{ "id": "11111111-1111-4111-8111-111111111109", "kind": "entity", "name": "Audit", "formatVersion": 1 }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticCodes.SchemaViolation, diagnostic.Code));
    }

    [Theory]
    [InlineData("""{ "entity": "Request", "actions": ["read", "start"] }""", "/grants/0/actions/1", "An entity grant cannot allow 'start'. It takes only 'read', 'create', 'update' and 'delete'.")]
    [InlineData("""{ "dataSource": "RequestList", "actions": ["read", "update"] }""", "/grants/0/actions/1", "A data source grant cannot allow 'update'. It takes only 'read'.")]
    [InlineData("""{ "process": "RequestReview", "actions": ["read"] }""", "/grants/0/actions/0", "A process grant cannot allow 'read'. It takes only 'start', 'inspect' and 'cancel'.")]
    [InlineData("""{ "endpoint": "SubmitRequest", "actions": ["invoke", "read"] }""", "/grants/0/actions/1", "An endpoint grant cannot allow 'read'. It takes only 'invoke'.")]
    [InlineData("""{ "entity": "Request", "actions": ["inspect"] }""", "/grants/0/actions/0", "An entity grant cannot allow 'inspect'. It takes only 'read', 'create', 'update' and 'delete'.")]
    [InlineData("""{ "endpoint": "SubmitRequest", "actions": ["inspect"] }""", "/grants/0/actions/0", "An endpoint grant cannot allow 'inspect'. It takes only 'invoke'.")]
    [InlineData("""{ "process": "RequestReview", "actions": ["invoke"] }""", "/grants/0/actions/0", "A process grant cannot allow 'invoke'. It takes only 'start', 'inspect' and 'cancel'.")]
    [InlineData("""{ "endpoint": "SubmitRequest", "actions": ["invoke"], "filter": "true" }""", "/grants/0/filter", "An endpoint grant has no 'filter'. Only an entity grant limits rows.")]
    [InlineData("""{ "endpoint": "SubmitRequest", "actions": ["invoke"], "fields": ["owner"] }""", "/grants/0/fields", "An endpoint grant has no 'fields'. Only an entity grant limits fields.")]
    [InlineData("""{ "process": "RequestReview", "endpoint": "SubmitRequest", "actions": ["start"] }""", "/grants/0", "A grant names exactly one target: an 'entity', a 'dataSource', a 'process' or an 'endpoint'. This one names 2.")]
    [InlineData("""{ "actions": ["read"] }""", "/grants/0", "A grant names exactly one target: an 'entity', a 'dataSource', a 'process' or an 'endpoint'. This one names 0.")]
    [InlineData("""{ "entity": "Request", "dataSource": "RequestList", "actions": ["read"], "filter": "nope" }""", "/grants/0", "A grant names exactly one target: an 'entity', a 'dataSource', a 'process' or an 'endpoint'. This one names 2.")]
    [InlineData("""{ "dataSource": "RequestList", "actions": ["read"], "filter": "true" }""", "/grants/0/filter", "A data source grant has no 'filter'. Only an entity grant limits rows.")]
    [InlineData("""{ "process": "RequestReview", "actions": ["start"], "fields": ["owner"] }""", "/grants/0/fields", "A process grant has no 'fields'. Only an entity grant limits fields.")]
    public void A_badly_shaped_grant_is_an_invalid_grant_at_its_path(string grant, string path, string message)
    {
        using var folder = Folder(Policy("""["employee"]""", $"[{grant}]"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidGrant, PolicyFile, path, message), (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.Message));
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_action_outside_the_schema_enum_is_a_schema_violation()
    {
        using var folder = Folder(Policy("""["employee"]""", """[{ "process": "RequestReview", "actions": ["frobnicate"] }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticCodes.SchemaViolation, diagnostic.Code));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_start_and_inspect_grant_on_a_process_compiles_with_both_actions()
    {
        using var folder = Folder(Policy("""["employee"]""", """[{ "process": "RequestReview", "actions": ["start", "inspect"] }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var grant = Assert.Single(Assert.Single(result.Model!.Policies).Grants);
        Assert.Equal(new ProcessReference(Guid.Parse(ProcessId), "RequestReview"), grant.Process);
        Assert.Equal([PolicyAction.Start, PolicyAction.Inspect], grant.Actions);
    }

    [Fact]
    public void A_cancel_grant_on_a_process_compiles()
    {
        using var folder = Folder(Policy("""["employee"]""", """[{ "process": "RequestReview", "actions": ["cancel"] }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var grant = Assert.Single(Assert.Single(result.Model!.Policies).Grants);
        Assert.Equal([PolicyAction.Cancel], grant.Actions);
    }

    [Fact]
    public void An_invoke_grant_on_an_endpoint_compiles_with_the_endpoint_resolved()
    {
        using var folder = Folder(Policy("""["employee"]""", """[{ "endpoint": "submitrequest", "actions": ["invoke"] }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var grant = Assert.Single(Assert.Single(result.Model!.Policies).Grants);
        Assert.Equal(
            (null, null, null, new EndpointReference(Guid.Parse(EndpointId), "SubmitRequest"), null, null),
            (grant.Entity, grant.DataSource, grant.Process, grant.Endpoint, grant.Filter, grant.Fields));
        Assert.Equal([PolicyAction.Invoke], grant.Actions);

        // An endpoint grant reaches no record and starts no process on its own.
        Assert.True(result.Model.TryGetEntity("Request", out var request));
        Assert.False(PolicyEvaluator.Access(result.Model, ["employee"], request, PolicyAction.Read).Granted);
        Assert.True(result.Model.TryGetProcess("RequestReview", out var process));
        Assert.False(PolicyEvaluator.AllowsStart(result.Model, ["employee"], process));
    }

    [Fact]
    public void An_endpoint_whose_file_was_not_loaded_is_not_reported_again()
    {
        using var folder = Folder(Policy("""["employee"]""", """[{ "endpoint": "Broken", "actions": ["invoke"] }]"""))
            .With("endpoints/broken.json", """{ "id": "bc241668-fe85-45a4-b7cf-d36da8d56b79", "kind": "endpoint", "name": "Broken", "formatVersion": 1 }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, "endpoints/broken.json"), (diagnostic.Code, diagnostic.File)));
    }

    [Fact]
    public void A_grant_on_a_child_entity_is_reported_at_its_entity_naming_the_owner()
    {
        using var folder = Folder(Policy("""["employee"]""", """[{ "entity": "requestLine", "actions": ["read"], "fields": ["nope"], "filter": "nope" }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        // The fields and the filter are not checked against a child entity.
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.GrantOnChildEntity, "/grants/0/entity", "The entity 'RequestLine' is a child entity owned by 'Request.lines'. A grant names a root entity. The owner's grant covers its child rows."),
            (diagnostic.Code, diagnostic.Path, diagnostic.Message));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("amount + 1", ExpressionDiagnosticCodes.ResultTypeMismatch, "boolean")]
    [InlineData("lower(owner) == 'a'", ExpressionDiagnosticCodes.OutsideSqlSubset, "Function 'lower'")]
    [InlineData("count(lines) > 0", ExpressionDiagnosticCodes.OutsideSqlSubset, "Function 'count'")]
    [InlineData("owner ==", ExpressionDiagnosticCodes.SyntaxError, "character")]
    [InlineData("nope == currentUser()", ExpressionDiagnosticCodes.UnknownName, "'nope'")]
    public void A_filter_that_is_not_boolean_or_not_in_the_sql_subset_is_reported_at_the_filter(string filter, string code, string messagePart)
    {
        using var folder = Folder(Policy("""["employee"]""", $$"""[{ "entity": "Request", "actions": ["read"], "filter": "{{filter}}" }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, PolicyFile, "/grants/0/filter"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    public static TheoryData<string, string, string, string> CurrentUserOutsidePolicyFilters => new()
    {
        { "entities/request.json", Request("""[{ "expression": "owner == currentUser()", "message": { "textKey": "request.invalid" }, "field": "owner" }]"""), "/validations/0/expression", "at character 10." },
        { "data-sources/request-list.json", RequestList("owner == currentUser()"), "/filter", "at character 10." },
        { "processes/request-review.json", RequestReview("currentUser() != null"), "/startCondition/expression", "at character 1." },
        { "rules/is-owner.json", IsOwner("owner == currentUser()"), "/expression", "at character 10." },
    };

    [Theory]
    [MemberData(nameof(CurrentUserOutsidePolicyFilters))]
    public void Current_user_outside_a_policy_filter_is_reported_at_the_call(string file, string content, string path, string position)
    {
        using var folder = Folder(Policy("""["employee"]""", """[{ "process": "RequestReview", "actions": ["start"] }]""")).With(file, content);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((ExpressionDiagnosticCodes.CurrentUserOutsidePolicyFilter, file, path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal($"Function 'currentUser' is allowed only in a policy filter {position}", diagnostic.Message);
        Assert.Null(result.Model);
    }

    private const string FilteredGrants = """
        [
          { "entity": "Request", "actions": ["read"], "filter": "owner == currentUser()" },
          { "dataSource": "RequestList", "actions": ["read"] },
          { "entity": "Request", "actions": ["read", "update"], "filter": "amount > 10" },
          { "entity": "Request", "actions": ["update", "delete"], "fields": ["owner"] }
        ]
        """;

    [Fact]
    public void Access_lists_the_filter_of_each_matching_grant_with_its_policy_and_index()
    {
        using var folder = Folder(Policy("""["employee"]""", FilteredGrants));
        var model = Compile(folder);
        Assert.True(model.TryGetEntity("Request", out var request));

        var read = PolicyEvaluator.Access(model, ["employee"], request, PolicyAction.Read);
        var update = PolicyEvaluator.Access(model, ["employee"], request, PolicyAction.Update);

        Assert.True(read.Granted);
        Assert.Equal(
            [("RequestAccess", 0, "owner == currentUser()"), ("RequestAccess", 2, "amount > 10")],
            read.Filters!.Select(filter => (filter.Policy, filter.Grant, filter.Expression.Expression)));

        // The grant with a fields list counts, and it has no filter, so it reaches every record.
        Assert.Equal(new RecordAccess(true, null), update);
        Assert.Equal(new RecordAccess(true, null), PolicyEvaluator.Access(model, ["employee"], request, PolicyAction.Delete));

        // A filtered read grant on the root entity is enough for the data source check, which
        // leaves the filters to the query.
        Assert.True(model.TryGetDataSource("RequestList", out var requestList));
        Assert.True(PolicyEvaluator.AllowsRead(model, ["employee"], requestList));
    }

    [Theory]
    [InlineData("employee", PolicyAction.Create)]
    [InlineData("Employee", PolicyAction.Read)]
    [InlineData("manager", PolicyAction.Read)]
    public void Access_without_a_matching_grant_is_not_granted_and_reaches_no_record(string role, PolicyAction action)
    {
        using var folder = Folder(Policy("""["employee"]""", FilteredGrants));
        var model = Compile(folder);
        Assert.True(model.TryGetEntity("Request", out var request));

        var access = PolicyEvaluator.Access(model, [role], request, action);

        Assert.False(access.Granted);
        Assert.Empty(access.Filters!);
    }

    [Fact]
    public void Access_reaches_every_record_when_any_matching_grant_has_no_filter()
    {
        using var folder = Folder(Policy("""["employee"]""", """
            [
              { "entity": "Request", "actions": ["read"], "filter": "owner == currentUser()" },
              { "entity": "Request", "actions": ["read"] }
            ]
            """));
        var model = Compile(folder);
        Assert.True(model.TryGetEntity("Request", out var request));

        var access = PolicyEvaluator.Access(model, ["employee"], request, PolicyAction.Read);

        Assert.Equal(new RecordAccess(true, null), access);
    }

    [Fact]
    public void Fields_are_the_union_over_the_matching_grants_and_every_field_when_one_has_no_fields()
    {
        const string Limited = """
            { "entity": "Request", "actions": ["read"], "fields": ["owner"] },
            { "entity": "Request", "actions": ["read", "update"], "filter": "amount > 10", "fields": ["AMOUNT"] },
            { "entity": "Request", "actions": ["create"] }
            """;
        using var limited = Folder(Policy("""["employee"]""", $"[{Limited}]"));
        using var unlimited = Folder(Policy("""["employee"]""", $$"""[{{Limited}}, { "entity": "Request", "actions": ["read"] }]"""));
        var model = Compile(limited);
        Assert.True(model.TryGetEntity("Request", out var request));
        var everyField = Compile(unlimited);
        Assert.True(everyField.TryGetEntity("Request", out var unlimitedRequest));

        // The fields are the declared names, whatever the filters.
        Assert.Equal(["amount", "owner"], PolicyEvaluator.Fields(model, ["employee"], request, PolicyAction.Read)!.Order(StringComparer.Ordinal));
        Assert.Equal(["amount"], PolicyEvaluator.Fields(model, ["employee"], request, PolicyAction.Update)!);
        Assert.Null(PolicyEvaluator.Fields(model, ["employee"], request, PolicyAction.Create));
        Assert.Empty(PolicyEvaluator.Fields(model, ["employee"], request, PolicyAction.Delete)!);
        Assert.Empty(PolicyEvaluator.Fields(model, ["manager"], request, PolicyAction.Read)!);
        Assert.Null(PolicyEvaluator.Fields(everyField, ["employee"], unlimitedRequest, PolicyAction.Read));
    }

    [Fact]
    public void Start_is_allowed_only_by_a_start_grant_on_the_process_for_a_declared_role_held()
    {
        using var granted = Folder(Policy("""["employee"]""", """[{ "process": "RequestReview", "actions": ["start"] }]"""));
        using var entityOnly = Folder(Policy("""["employee"]""", """[{ "entity": "Request", "actions": ["read", "create", "update", "delete"] }]"""));
        var model = Compile(granted);
        Assert.True(model.TryGetProcess("RequestReview", out var process));
        var withoutStart = Compile(entityOnly);
        Assert.True(withoutStart.TryGetProcess("RequestReview", out var otherProcess));

        Assert.True(PolicyEvaluator.AllowsStart(model, ["employee"], process));
        Assert.False(PolicyEvaluator.AllowsStart(model, ["manager"], process));
        Assert.False(PolicyEvaluator.AllowsStart(model, ["Employee"], process));
        Assert.False(PolicyEvaluator.AllowsStart(withoutStart, ["employee"], otherProcess));
    }

    [Fact]
    public void Inspect_is_allowed_only_by_an_inspect_grant_on_the_process_for_a_declared_role_held()
    {
        using var granted = Folder(Policy("""["employee"]""", """[{ "process": "RequestReview", "actions": ["start", "inspect"] }]"""));
        using var startOnly = Folder(Policy("""["employee"]""", """[{ "process": "RequestReview", "actions": ["start"] }]"""));
        var model = Compile(granted);
        Assert.True(model.TryGetProcess("RequestReview", out var process));
        var withoutInspect = Compile(startOnly);
        Assert.True(withoutInspect.TryGetProcess("RequestReview", out var otherProcess));

        Assert.True(PolicyEvaluator.AllowsInspect(model, ["employee"], process));
        Assert.False(PolicyEvaluator.AllowsInspect(model, ["manager"], process));
        Assert.False(PolicyEvaluator.AllowsInspect(model, ["Employee"], process));
        Assert.False(PolicyEvaluator.AllowsInspect(withoutInspect, ["employee"], otherProcess));
        Assert.True(PolicyEvaluator.AllowsStart(withoutInspect, ["employee"], otherProcess));
    }

    [Fact]
    public void Cancel_is_allowed_only_by_a_cancel_grant_on_the_process_for_a_declared_role_held()
    {
        using var granted = Folder(Policy("""["employee"]""", """[{ "process": "RequestReview", "actions": ["start", "cancel"] }]"""));
        using var startOnly = Folder(Policy("""["employee"]""", """[{ "process": "RequestReview", "actions": ["start"] }]"""));
        var model = Compile(granted);
        Assert.True(model.TryGetProcess("RequestReview", out var process));
        var withoutCancel = Compile(startOnly);
        Assert.True(withoutCancel.TryGetProcess("RequestReview", out var otherProcess));

        Assert.True(PolicyEvaluator.AllowsCancel(model, ["employee"], process));
        Assert.False(PolicyEvaluator.AllowsCancel(model, ["manager"], process));
        Assert.False(PolicyEvaluator.AllowsCancel(model, ["Employee"], process));
        Assert.False(PolicyEvaluator.AllowsCancel(withoutCancel, ["employee"], otherProcess));
        Assert.False(PolicyEvaluator.AllowsInspect(model, ["employee"], process));
    }

    private static ApplicationModel Compile(TemporaryFolder folder)
    {
        var result = ApplicationCompiler.Compile(folder.Path);
        Assert.Empty(result.Diagnostics);
        return result.Model!;
    }

    private static string Policy(string roles, string grants) => $$"""
        { "id": "{{PolicyId}}", "kind": "policy", "name": "RequestAccess", "formatVersion": 1, "roles": {{roles}}, "grants": {{grants}} }
        """;

    /// <summary>The root entity <c>Request</c>, with the given validations.</summary>
    private static string Request(string validations = "[]") => $$"""
        { "id": "{{RequestId}}", "kind": "entity", "name": "Request", "formatVersion": 1,
          "fields": [
            { "name": "owner", "type": "text" },
            { "name": "amount", "type": "decimal" },
            { "name": "department", "type": "reference", "target": "Department" },
            { "name": "lines", "type": "child-collection", "target": "RequestLine" }
          ],
          "validations": {{validations}} }
        """;

    private static string RequestList(string? filter = null) => $$"""
        { "id": "{{DataSourceId}}", "kind": "dataSource", "name": "RequestList", "formatVersion": 1, "entity": "Request",
          "fields": [{ "name": "owner", "path": "owner" }]{{(filter is null ? "" : $", \"filter\": \"{filter}\"")}} }
        """;

    private static string RequestReview(string? startCondition = null) => $$"""
        { "id": "{{ProcessId}}", "kind": "process", "name": "RequestReview", "formatVersion": 1, "entity": "Request",
          {{(startCondition is null ? "" : $"\"startCondition\": {{ \"expression\": \"{startCondition}\", \"message\": {{ \"textKey\": \"request.cannotStart\" }} }},")}}
          "start": "done", "steps": [{ "name": "done", "type": "end" }] }
        """;

    private static string IsOwner(string expression) => $$"""
        { "id": "66666666-6666-4666-8666-666666666601", "kind": "rule", "name": "IsOwner", "formatVersion": 1,
          "parameters": [{ "name": "owner", "type": "text" }, { "name": "user", "type": "text" }], "resultType": "boolean", "expression": "{{expression}}" }
        """;

    /// <summary>
    /// An application with the roles <c>employee</c> and <c>manager</c>, a root entity <c>Request</c> that
    /// references a <c>Department</c> and owns <c>RequestLine</c> rows, the data source <c>RequestList</c>
    /// and the process <c>RequestReview</c> over it, the endpoint <c>SubmitRequest</c> that starts it,
    /// the rule <c>IsOwner</c>, and <paramref name="policy"/>.
    /// </summary>
    private static TemporaryFolder Folder(string policy) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", Texts)
            .With("roles/employee.json", """
                { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "employee", "formatVersion": 1, "label": { "textKey": "role.employee" } }
                """)
            .With("roles/manager.json", """
                { "id": "44444444-4444-4444-8444-444444444402", "kind": "role", "name": "manager", "formatVersion": 1, "label": { "textKey": "role.manager" } }
                """)
            .With("entities/request.json", Request())
            .With("entities/department.json", """
                { "id": "11111111-1111-4111-8111-111111111102", "kind": "entity", "name": "Department", "formatVersion": 1,
                  "displayField": "name", "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """)
            .With("entities/request-line.json", """
                { "id": "11111111-1111-4111-8111-111111111103", "kind": "entity", "name": "RequestLine", "formatVersion": 1,
                  "fields": [ { "name": "quantity", "type": "integer" } ] }
                """)
            .With("data-sources/request-list.json", RequestList())
            .With("processes/request-review.json", RequestReview())
            .With("rules/is-owner.json", IsOwner("owner == user"))
            .With("endpoints/submit-request.json", $$"""
                { "id": "{{EndpointId}}", "kind": "endpoint", "name": "SubmitRequest", "formatVersion": 1, "entity": "Request",
                  "process": "RequestReview", "input": [{ "name": "owner", "type": "text" }], "values": { "owner": "owner" } }
                """)
            .With(PolicyFile, policy);
}
