using System.Diagnostics;
using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Policy;

namespace Axis.Configuration.Tests;

/// <summary>
/// Evaluating the grants of one request stays cheap for a user with many roles under many
/// policies, so computing them once per request, not once per record, is enough.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class PolicyEvaluationPerformanceTests
{
    private const int RoleCount = 10;
    private const int PolicyCount = 50;
    private const int Requests = 1_000;

    // One request's evaluation took about 60 microseconds locally. 1 ms per request leaves room
    // for shared CI runners. Evaluation takes no record, so its cost does not depend on how many
    // records the query then reads.
    private static readonly TimeSpan _evaluationTime = TimeSpan.FromSeconds(1);

    private static readonly string[] _roles = [.. Enumerable.Range(0, RoleCount).Select(index => $"role{index}")];

    [Fact]
    public void A_thousand_grant_evaluations_for_ten_roles_under_fifty_policies_finish_within_the_time_limit()
    {
        using var folder = Folder();
        var result = ApplicationCompiler.Compile(folder.Path);
        Assert.Empty(result.Diagnostics);
        var model = result.Model!;
        Assert.True(model.TryGetEntity("Request", out var request));
        Assert.True(model.TryGetEntity("Department", out var department));
        Assert.True(model.TryGetDataSource("RequestList", out var dataSource));
        Evaluate(model, request, department, dataSource);

        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < Requests; index++)
        {
            Evaluate(model, request, department, dataSource);
        }

        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"{Requests} evaluations took {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _evaluationTime, $"{Requests} evaluations took {stopwatch.Elapsed}.");

        // Every policy's read filter comes back in one list, which the server turns into one SQL condition.
        var read = PolicyEvaluator.Access(model, _roles, request, PolicyAction.Read);
        Assert.True(read.Granted);
        Assert.NotNull(read.Filters);
        Assert.Equal(PolicyCount, read.Filters.Count);
    }

    /// <summary>
    /// What the record list and the data source endpoints evaluate for one request: the read
    /// access and fields of the root entity, the fields of the referenced entity and the data
    /// source grant.
    /// </summary>
    private static void Evaluate(ApplicationModel model, EntityModel request, EntityModel department, DataSourceModel dataSource)
    {
        var access = PolicyEvaluator.Access(model, _roles, request, PolicyAction.Read);
        var requestFields = PolicyEvaluator.Fields(model, _roles, request, PolicyAction.Read);
        var departmentFields = PolicyEvaluator.Fields(model, _roles, department, PolicyAction.Read);
        var allowed = PolicyEvaluator.AllowsRead(model, _roles, dataSource);
        Assert.True(access.Granted && allowed && requestFields is not null && departmentFields is not null);
    }

    private static string Role(int index) => $$"""
        { "id": "44444444-4444-4444-8444-4444444444{{index:00}}", "kind": "role", "name": "role{{index}}", "formatVersion": 1,
          "label": { "textKey": "role.role{{index}}" } }
        """;

    // Policy i names two neighbouring roles, so each role is in ten policies.
    private static string Policy(int index) => $$"""
        { "id": "12121212-1212-4212-8212-1212121212{{index:00}}", "kind": "policy", "name": "Policy{{index:00}}", "formatVersion": 1,
          "roles": ["role{{index % RoleCount}}", "role{{(index + 1) % RoleCount}}"],
          "grants": [
            { "entity": "Request", "actions": ["read"], "filter": "department.manager == currentUser() or amount > {{index}}",
              "fields": ["title", "amount", "department"] },
            { "entity": "Department", "actions": ["read"], "fields": ["name"] },
            { "dataSource": "RequestList", "actions": ["read"] }
          ] }
        """;

    /// <summary>
    /// An application with the roles <c>role0</c> to <c>role9</c>, a <c>Request</c> that references
    /// a <c>Department</c>, the data source <c>RequestList</c> over it, and fifty policies.
    /// </summary>
    private static TemporaryFolder Folder()
    {
        var labels = string.Join(", ", Enumerable.Range(0, RoleCount).Select(index => $"\"role.role{index}\": \"Role {index}\""));
        var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", $$"""
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { {{labels}} } }
                """)
            .With("entities/request.json", """
                { "id": "11111111-1111-4111-8111-111111111101", "kind": "entity", "name": "Request", "formatVersion": 1,
                  "fields": [
                    { "name": "title", "type": "text", "required": true },
                    { "name": "amount", "type": "integer" },
                    { "name": "department", "type": "reference", "target": "Department" }
                  ] }
                """)
            .With("entities/department.json", """
                { "id": "11111111-1111-4111-8111-111111111102", "kind": "entity", "name": "Department", "formatVersion": 1,
                  "displayField": "name", "fields": [ { "name": "name", "type": "text", "required": true }, { "name": "manager", "type": "text" } ] }
                """)
            .With("data-sources/request-list.json", """
                { "id": "99999999-9999-4999-8999-999999999901", "kind": "dataSource", "name": "RequestList", "formatVersion": 1, "entity": "Request",
                  "fields": [{ "name": "title", "path": "title" }, { "name": "amount", "path": "amount" }] }
                """);
        for (var index = 0; index < RoleCount; index++)
        {
            folder.With($"roles/role{index}.json", Role(index));
        }

        for (var index = 0; index < PolicyCount; index++)
        {
            folder.With($"policies/policy{index:00}.json", Policy(index));
        }

        return folder;
    }
}
