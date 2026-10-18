using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the policies of an application and builds the checked ones. Each role a policy names is
/// a declared role, matched exactly. Each grant has exactly one target: an <c>entity</c>, a
/// <c>dataSource</c>, a <c>process</c> or an <c>endpoint</c>. Its actions are ones that target
/// takes: <c>read</c>, <c>create</c>, <c>update</c> and <c>delete</c> for an entity, <c>read</c> for
/// a data source, <c>start</c>, <c>inspect</c>, <c>cancel</c>, <c>retry</c> and <c>resolve</c> for a process and <c>invoke</c> for an endpoint. Only an entity grant
/// has a <c>filter</c> or <c>fields</c>. The target
/// is loaded, ignoring letter case, and an entity target is not a child entity. Each of its
/// <c>fields</c> is a field of the entity, ignoring letter case. Its <c>filter</c> parses,
/// type-checks as a boolean over the entity's fields, paths through reference fields and the named
/// rules, can call <c>currentUser()</c>, and translates to SQL. Every problem of a policy is
/// reported in one pass. A grant without exactly one target is not checked further, and neither is
/// a grant whose target is unknown or a child entity. A role or target file that was not loaded
/// because of its own errors is not reported again. A data source grant that names a module the
/// application does not declare is <c>AXC0207</c>, in place of the unknown-target diagnostic.
/// </summary>
internal static class PolicyChecker
{
    private static readonly PolicyAction[] _entityActions = [PolicyAction.Read, PolicyAction.Create, PolicyAction.Update, PolicyAction.Delete];
    private static readonly PolicyAction[] _dataSourceActions = [PolicyAction.Read];
    private static readonly PolicyAction[] _processActions = [PolicyAction.Start, PolicyAction.Inspect, PolicyAction.Cancel, PolicyAction.Retry, PolicyAction.Resolve];
    private static readonly PolicyAction[] _endpointActions = [PolicyAction.Invoke];

    /// <summary>
    /// Checks <paramref name="policies"/>, which come in path order. <paramref name="roleNames"/>
    /// holds the declared role names, compared exactly. <paramref name="ownerOf"/> names the owner of
    /// a child entity, or gives null for a root entity.
    /// </summary>
    public static void Check(
        IReadOnlyList<PolicyResource> policies,
        IReadOnlySet<string> roleNames,
        IReadOnlySet<string> unloadedRoleNames,
        Func<string, EntityResource?> findEntity,
        Func<string, string?> ownerOf,
        IReadOnlySet<string> unloadedEntityNames,
        Func<string, DataSourceResource?> findDataSource,
        IReadOnlySet<string> unloadedDataSourceNames,
        Func<string, ProcessResource?> findProcess,
        IReadOnlySet<string> unloadedProcessNames,
        Func<string, EndpointResource?> findEndpoint,
        IReadOnlySet<string> unloadedEndpointNames,
        IEnumerable<ExpressionRule> rules,
        ModuleReferences moduleReferences,
        IReadOnlySet<string> modules,
        List<Diagnostic> diagnostics)
    {
        foreach (var policy in policies)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, policy.File, path, policy.Id));

            for (var index = 0; index < policy.Roles.Count; index++)
            {
                var role = policy.Roles[index];
                if (!roleNames.Contains(role) && !unloadedRoleNames.Contains(role))
                {
                    Report(DiagnosticCodes.UnknownPolicyRole, $"The role '{role}' was not found. No declared role has that name.", $"/roles/{index}");
                }
            }

            for (var index = 0; index < policy.Grants.Count; index++)
            {
                CheckGrant(
                    policy.Grants[index],
                    $"/grants/{index}",
                    findEntity,
                    ownerOf,
                    unloadedEntityNames,
                    findDataSource,
                    unloadedDataSourceNames,
                    findProcess,
                    unloadedProcessNames,
                    findEndpoint,
                    unloadedEndpointNames,
                    rules,
                    moduleReferences,
                    modules,
                    Report);
            }
        }
    }

    /// <summary>
    /// Builds a checked policy. Each grant's target is resolved, its fields are the entity's declared
    /// names, and its filter is compiled over the built entity's fields, paths through reference
    /// fields and the named rules, as a data source filter is.
    /// </summary>
    public static PolicyModel Build(
        PolicyResource policy,
        IReadOnlyList<EntityModel> entities,
        Func<string, DataSourceResource?> findDataSource,
        Func<string, ProcessResource?> findProcess,
        Func<string, EndpointResource?> findEndpoint,
        IEnumerable<ExpressionRule> rules,
        IReadOnlySet<string> modules)
    {
        EntityModel? Find(string name) =>
            entities.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

        GrantModel BuildGrant(GrantDefinition grant)
        {
            var actions = grant.Actions.Select(ParseAction).ToList();
            if (grant.DataSource is { } dataSourceName)
            {
                var dataSource = findDataSource(dataSourceName)!;
                return new GrantModel(null, new DataSourceReference(dataSource.Id, dataSource.Name), null, null, actions, null, null);
            }

            if (grant.Process is { } processName)
            {
                var process = findProcess(processName)!;
                return new GrantModel(null, null, new ProcessReference(process.Id, process.Name), null, actions, null, null);
            }

            if (grant.Endpoint is { } endpointName)
            {
                var endpoint = findEndpoint(endpointName)!;
                return new GrantModel(null, null, null, new EndpointReference(endpoint.Id, endpoint.Name), actions, null, null);
            }

            var entity = Find(grant.Entity!)!;
            return new GrantModel(
                new EntityReference(entity.Id, entity.Name),
                null,
                null,
                null,
                actions,
                grant.Filter is { } filter
                    ? ExpressionModel.Compile(
                        filter, ExpressionScopes.ForDataSource(entity.Fields, [], Find, rules, modules), ExpressionType.Boolean, policyFilter: true)
                    : null,
                // A checked grant names only fields of the entity.
                grant.Fields?.Select(name => entity.Fields.First(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)).Name).ToList());
        }

        return new PolicyModel
        {
            Id = policy.Id,
            Name = policy.Name,
            File = policy.File,
            Roles = policy.Roles,
            Grants = policy.Grants.Select(BuildGrant).ToList(),
        };
    }

    private static void CheckGrant(
        GrantDefinition grant,
        string path,
        Func<string, EntityResource?> findEntity,
        Func<string, string?> ownerOf,
        IReadOnlySet<string> unloadedEntityNames,
        Func<string, DataSourceResource?> findDataSource,
        IReadOnlySet<string> unloadedDataSourceNames,
        Func<string, ProcessResource?> findProcess,
        IReadOnlySet<string> unloadedProcessNames,
        Func<string, EndpointResource?> findEndpoint,
        IReadOnlySet<string> unloadedEndpointNames,
        IEnumerable<ExpressionRule> rules,
        ModuleReferences moduleReferences,
        IReadOnlySet<string> modules,
        Action<string, string, string> report)
    {
        var targets = new[] { grant.Entity, grant.DataSource, grant.Process, grant.Endpoint }.Count(target => target is not null);
        if (targets != 1)
        {
            // Without exactly one target the actions, the filter and the fields mean nothing.
            report(
                DiagnosticCodes.InvalidGrant,
                $"A grant names exactly one target: an 'entity', a 'dataSource', a 'process' or an 'endpoint'. This one names {targets}.",
                path);
            return;
        }

        var (kind, described, name, allowed) = grant switch
        {
            { Entity: { } entity } => ("entity", "An entity grant", entity, _entityActions),
            { DataSource: { } dataSource } => ("dataSource", "A data source grant", dataSource, _dataSourceActions),
            { Process: { } process } => ("process", "A process grant", process, _processActions),
            _ => ("endpoint", "An endpoint grant", grant.Endpoint!, _endpointActions),
        };

        for (var index = 0; index < grant.Actions.Count; index++)
        {
            var action = grant.Actions[index];
            if (!allowed.Contains(ParseAction(action)))
            {
                var names = allowed.Select(allowedAction => $"'{Name(allowedAction)}'").ToList();
                var takes = names.Count == 1 ? names[0] : $"{string.Join(", ", names[..^1])} and {names[^1]}";
                report(
                    DiagnosticCodes.InvalidGrant,
                    $"{described} cannot allow '{action}'. It takes only {takes}.",
                    $"{path}/actions/{index}");
            }
        }

        if (kind != "entity")
        {
            if (grant.Filter is not null)
            {
                report(DiagnosticCodes.InvalidGrant, $"{described} has no 'filter'. Only an entity grant limits rows.", $"{path}/filter");
            }

            if (grant.Fields is not null)
            {
                report(DiagnosticCodes.InvalidGrant, $"{described} has no 'fields'. Only an entity grant limits fields.", $"{path}/fields");
            }
        }

        // A target file that was not loaded because of its own errors is not reported again.
        switch (kind)
        {
            case "dataSource":
                if (findDataSource(name) is null
                    && !unloadedDataSourceNames.Contains(name)
                    && !moduleReferences.ReportUndeclared(name, $"{path}/dataSource", report))
                {
                    report(DiagnosticCodes.UnknownGrantTarget, $"The data source '{name}' was not found. No loaded data source has that name.", $"{path}/dataSource");
                }

                return;

            case "process":
                if (findProcess(name) is null && !unloadedProcessNames.Contains(name))
                {
                    report(DiagnosticCodes.UnknownGrantTarget, $"The process '{name}' was not found. No loaded process has that name.", $"{path}/process");
                }

                return;

            case "endpoint":
                if (findEndpoint(name) is null && !unloadedEndpointNames.Contains(name))
                {
                    report(DiagnosticCodes.UnknownGrantTarget, $"The endpoint '{name}' was not found. No loaded endpoint has that name.", $"{path}/endpoint");
                }

                return;
        }

        var entityResource = findEntity(name);
        if (entityResource is null)
        {
            if (!unloadedEntityNames.Contains(name))
            {
                report(DiagnosticCodes.UnknownGrantTarget, $"The entity '{name}' was not found. No loaded entity has that name.", $"{path}/entity");
            }

            return;
        }

        if (ownerOf(entityResource.Name) is { } owner)
        {
            // A child row is read and written only through its owner, so the owner's grant covers it.
            report(
                DiagnosticCodes.GrantOnChildEntity,
                $"The entity '{entityResource.Name}' is a child entity owned by '{owner}'. A grant names a root entity. The owner's grant covers its child rows.",
                $"{path}/entity");
            return;
        }

        var fields = grant.Fields ?? [];
        for (var index = 0; index < fields.Count; index++)
        {
            if (ApplicationCompiler.FindField(entityResource, fields[index]) is null)
            {
                report(
                    DiagnosticCodes.UnknownGrantField,
                    $"The field '{fields[index]}' was not found. The entity '{entityResource.Name}' has no field with that name.",
                    $"{path}/fields/{index}");
            }
        }

        if (grant.Filter is { } filter
            && ApplicationCompiler.CheckFilter(filter, ExpressionScopes.ForDataSource(entityResource.Fields, [], findEntity, rules, modules), policyFilter: true) is { } problem)
        {
            report(problem.Code, problem.Message, $"{path}/filter");
        }
    }

    private static PolicyAction ParseAction(string action) =>
        action switch
        {
            "read" => PolicyAction.Read,
            "create" => PolicyAction.Create,
            "update" => PolicyAction.Update,
            "delete" => PolicyAction.Delete,
            "start" => PolicyAction.Start,
            "invoke" => PolicyAction.Invoke,
            "inspect" => PolicyAction.Inspect,
            "cancel" => PolicyAction.Cancel,
            "retry" => PolicyAction.Retry,
            "resolve" => PolicyAction.Resolve,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "The JSON Schema allows only read, create, update, delete, start, invoke, inspect, cancel, retry and resolve."),
        };

    private static string Name(PolicyAction action) => action.ToString().ToLowerInvariant();
}
