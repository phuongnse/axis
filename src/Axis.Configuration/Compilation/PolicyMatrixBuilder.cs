using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Builds the <see cref="PolicyMatrix"/> from the resources a compile loaded. It reads the resources, not the
/// compiled model, because the model exists only when the folder has no errors. It repeats the matching of
/// <c>PolicyEvaluator</c>: a role gets the grants of every policy that names it, combined with OR.
/// </summary>
internal static class PolicyMatrixBuilder
{
    private const string EntityKind = "entity";
    private const string DataSourceKind = "dataSource";
    private const string ProcessKind = "process";
    private const string EndpointKind = "endpoint";
    private const string ReadAction = "read";
    private const string CreateAction = "create";
    private const string ExportAction = "export";
    private const string ImportAction = "import";

    private static readonly IReadOnlyList<string> _entityActions = PolicyActions.Of(EntityKind);
    private static readonly IReadOnlyList<string> _dataSourceActions = PolicyActions.Of(DataSourceKind);
    private static readonly IReadOnlyList<string> _processActions = PolicyActions.Of(ProcessKind);
    private static readonly IReadOnlyList<string> _endpointActions = PolicyActions.Of(EndpointKind);

    public static PolicyMatrix Build(
        IReadOnlyList<RoleResource> roles,
        IReadOnlyList<PolicyResource> policies,
        IReadOnlyList<EntityResource> entities,
        IReadOnlySet<string> childEntityNames,
        IReadOnlyList<DataSourceResource> dataSources,
        IReadOnlyList<ProcessResource> processes,
        IReadOnlyList<EndpointResource> endpoints)
    {
        var targets = new List<PolicyMatrixTarget>();
        targets.AddRange(Targets(entities.Where(entity => !childEntityNames.Contains(entity.Name)), EntityKind, _entityActions, _ => null));
        targets.AddRange(Targets(dataSources, DataSourceKind, _dataSourceActions, dataSource => dataSource.Entity));
        targets.AddRange(Targets(processes, ProcessKind, _processActions, _ => null));
        targets.AddRange(Targets(endpoints, EndpointKind, _endpointActions, _ => null));

        var columnByKey = new Dictionary<(string Kind, string Name), int>();
        for (var column = 0; column < targets.Count; column++)
        {
            columnByKey[(targets[column].Kind, targets[column].Name.ToUpperInvariant())] = column;
        }

        var entityColumns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var column = 0; column < targets.Count; column++)
        {
            if (targets[column].Kind == EntityKind)
            {
                entityColumns[targets[column].Name] = column;
            }
        }

        var rows = new List<PolicyMatrixRole>();
        var seenRoles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            if (seenRoles.Add(role.Name))
            {
                rows.Add(Row(role, policies, targets, columnByKey, entityColumns, dataSources));
            }
        }

        return new PolicyMatrix(targets, rows);
    }

    private static IEnumerable<PolicyMatrixTarget> Targets<T>(
        IEnumerable<T> resources,
        string kind,
        IReadOnlyList<string> actions,
        Func<T, string?> entityOf)
        where T : Resource
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in resources)
        {
            if (seen.Add(resource.Name))
            {
                yield return new PolicyMatrixTarget(kind, resource.Id, resource.Name, DefinedNamesBuilder.ModuleOf(resource), actions, entityOf(resource));
            }
        }
    }

    private static PolicyMatrixRole Row(
        RoleResource role,
        IReadOnlyList<PolicyResource> policies,
        List<PolicyMatrixTarget> targets,
        Dictionary<(string Kind, string Name), int> columnByKey,
        Dictionary<string, int> entityColumns,
        IReadOnlyList<DataSourceResource> dataSources)
    {
        var accessName = AccessPolicyName(role.Name);
        var access = policies.FirstOrDefault(policy => string.Equals(policy.Name, accessName, StringComparison.OrdinalIgnoreCase));
        var editable = access is null || (access.Roles.Count == 1 && access.Roles[0] == role.Name);

        var grantsByColumn = new List<PolicyMatrixGrant>[targets.Count];
        foreach (var policy in policies.Where(policy => policy.Roles.Contains(role.Name, StringComparer.Ordinal)))
        {
            for (var index = 0; index < policy.Grants.Count; index++)
            {
                var grant = policy.Grants[index];
                if (Target(grant) is not var (kind, name)
                    || !columnByKey.TryGetValue((kind, name.ToUpperInvariant()), out var column))
                {
                    continue;
                }

                (grantsByColumn[column] ??= []).Add(new PolicyMatrixGrant(
                    policy.Id,
                    policy.Name,
                    index,
                    grant.Actions,
                    grant.Filter,
                    grant.Fields,
                    Shared: !(ReferenceEquals(policy, access) && editable)));
            }
        }

        var cells = new PolicyMatrixCell[targets.Count];
        for (var column = 0; column < targets.Count; column++)
        {
            var target = targets[column];
            var grants = grantsByColumn[column] ?? [];
            var actions = target.Actions.Where(action => grants.Any(grant => grant.Actions.Contains(action, StringComparer.Ordinal))).ToList();
            cells[column] = new PolicyMatrixCell(target.Kind, target.Name, actions, null, grants);
        }

        // A data source read counts only when the role can also read the root entity.
        for (var column = 0; column < targets.Count; column++)
        {
            var target = targets[column];
            if (target.Kind != DataSourceKind || !cells[column].Actions.Contains(ReadAction))
            {
                continue;
            }

            var entityName = target.Entity!;
            if (entityColumns.TryGetValue(entityName, out var entityColumn))
            {
                if (cells[entityColumn].Actions.Contains(ReadAction))
                {
                    continue;
                }

                entityName = targets[entityColumn].Name;
            }

            cells[column] = cells[column] with
            {
                Actions = [.. cells[column].Actions.Where(action => action != ReadAction)],
                MissingEntityRead = entityName,
            };
        }

        // An export reaches only what read reaches and an import writes through create, as the evaluator decides.
        for (var column = 0; column < targets.Count; column++)
        {
            var actions = cells[column].Actions;
            var dropped = targets[column].Kind switch
            {
                EntityKind => actions.Where(action =>
                    (action == ExportAction && !actions.Contains(ReadAction)) || (action == ImportAction && !actions.Contains(CreateAction))),
                DataSourceKind => actions.Where(action => action == ExportAction && !actions.Contains(ReadAction)),
                _ => [],
            };
            var kept = actions.Except(dropped.ToList()).ToList();
            if (kept.Count != actions.Count)
            {
                cells[column] = cells[column] with { Actions = kept };
            }
        }

        return new PolicyMatrixRole(
            role.Id,
            role.Name,
            new PolicyMatrixAccessPolicy(accessName, access?.Id),
            editable,
            editable ? null : "accessPolicyShared",
            cells);
    }

    /// <summary>The kind and name a grant targets, or null unless it sets exactly one target.</summary>
    private static (string Kind, string Name)? Target(GrantDefinition grant)
    {
        var set = new List<(string Kind, string Name)>(1);
        if (grant.Entity is not null)
        {
            set.Add((EntityKind, grant.Entity));
        }

        if (grant.DataSource is not null)
        {
            set.Add((DataSourceKind, grant.DataSource));
        }

        if (grant.Process is not null)
        {
            set.Add((ProcessKind, grant.Process));
        }

        if (grant.Endpoint is not null)
        {
            set.Add((EndpointKind, grant.Endpoint));
        }

        return set.Count == 1 ? set[0] : null;
    }

    private static string AccessPolicyName(string role) =>
        string.Concat(role.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..])) + "Access";
}
