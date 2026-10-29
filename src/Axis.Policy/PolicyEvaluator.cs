using Axis.Configuration.Model;

namespace Axis.Policy;

/// <summary>
/// Decides whether a user's roles grant an action on an entity, a data source, a process or an
/// endpoint of an application. Access is denied unless a grant allows it. A role counts only when
/// a roles claim value, or a role that the application's identity provider mapping gave, equals the name of a role the
/// application declares, compared exactly, so any other value grants nothing. A policy applies when the user holds any of its roles. An entity grant with a
/// <see cref="GrantModel.Filter"/> grants the records that match it, and one with
/// <see cref="GrantModel.Fields"/> grants those fields, through <see cref="Access"/> and
/// <see cref="Fields"/>. The caller applies them: the record API and the data source endpoint
/// both add the filters to their SQL and check the fields.
/// </summary>
public static class PolicyEvaluator
{
    /// <summary>
    /// What <paramref name="roles"/> grant for <paramref name="action"/> on
    /// <paramref name="entity"/>. The access is granted when any grant for the action exists,
    /// whatever its <see cref="GrantModel.Fields"/>. It reaches every record when one of them has
    /// no filter, and otherwise the records that match any of their filters.
    /// </summary>
    public static RecordAccess Access(ApplicationModel application, IEnumerable<string> roles, EntityModel entity, PolicyAction action)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(entity);

        var filters = new List<RecordFilter>();
        foreach (var policy in Policies(application, roles))
        {
            for (var index = 0; index < policy.Grants.Count; index++)
            {
                var grant = policy.Grants[index];
                if (grant.Entity?.Id != entity.Id || !grant.Actions.Contains(action))
                {
                    continue;
                }

                if (grant.Filter is null)
                {
                    return new RecordAccess(true, null);
                }

                filters.Add(new RecordFilter(policy.Name, index, grant.Filter));
            }
        }

        return new RecordAccess(filters.Count > 0, filters);
    }

    /// <summary>
    /// The declared names of the fields of <paramref name="entity"/> that <paramref name="roles"/>
    /// grant <paramref name="action"/> on: the union of the <see cref="GrantModel.Fields"/> of the
    /// matching grants, whatever their filters. It is null, which means every field, when any
    /// matching grant has no fields, and empty when no grant matches.
    /// </summary>
    public static IReadOnlySet<string>? Fields(ApplicationModel application, IEnumerable<string> roles, EntityModel entity, PolicyAction action)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(entity);

        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in Grants(application, roles))
        {
            if (grant.Entity?.Id != entity.Id || !grant.Actions.Contains(action))
            {
                continue;
            }

            if (grant.Fields is null)
            {
                return null;
            }

            fields.UnionWith(grant.Fields);
        }

        return fields;
    }

    /// <summary>
    /// Whether <paramref name="roles"/> grant <c>read</c> on <paramref name="dataSource"/>. That
    /// takes a <c>read</c> grant on the data source and any <c>read</c> grant on its root entity,
    /// as <see cref="Access"/> decides, whatever its filter or fields. The caller limits the rows
    /// to the root entity's read filters and checks the projected fields.
    /// </summary>
    public static bool AllowsRead(ApplicationModel application, IEnumerable<string> roles, DataSourceModel dataSource)
    {
        var held = roles.ToList();
        return Grants(application, held).Any(grant => grant.DataSource?.Id == dataSource.Id && grant.Actions.Contains(PolicyAction.Read))
            && application.FindEntity(dataSource.Entity.Id) is { } entity
            && Access(application, held, entity, PolicyAction.Read).Granted;
    }

    /// <summary>
    /// Whether <paramref name="roles"/> grant <c>export</c> on <paramref name="entity"/>. That takes
    /// an <c>export</c> grant and a <c>read</c> grant on the entity, which may come from different
    /// roles, because an export reaches only what <c>read</c> reaches.
    /// </summary>
    public static bool AllowsExport(ApplicationModel application, IEnumerable<string> roles, EntityModel entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var held = roles.ToList();
        return Grants(application, held).Any(grant => grant.Entity?.Id == entity.Id && grant.Actions.Contains(PolicyAction.Export))
            && Access(application, held, entity, PolicyAction.Read).Granted;
    }

    /// <summary>
    /// Whether <paramref name="roles"/> grant <c>export</c> on <paramref name="dataSource"/>. That
    /// takes an <c>export</c> grant on the data source and everything <see cref="AllowsRead"/>
    /// needs, because an export reaches only what <c>read</c> reaches.
    /// </summary>
    public static bool AllowsExport(ApplicationModel application, IEnumerable<string> roles, DataSourceModel dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        var held = roles.ToList();
        return Grants(application, held).Any(grant => grant.DataSource?.Id == dataSource.Id && grant.Actions.Contains(PolicyAction.Export))
            && AllowsRead(application, held, dataSource);
    }

    /// <summary>
    /// Whether <paramref name="roles"/> grant <c>import</c> on <paramref name="entity"/>. That takes
    /// an <c>import</c> grant and a <c>create</c> grant on the entity, which may come from different
    /// roles, because an import writes through <c>create</c>.
    /// </summary>
    public static bool AllowsImport(ApplicationModel application, IEnumerable<string> roles, EntityModel entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var held = roles.ToList();
        return Grants(application, held).Any(grant => grant.Entity?.Id == entity.Id && grant.Actions.Contains(PolicyAction.Import))
            && Access(application, held, entity, PolicyAction.Create).Granted;
    }

    /// <summary>Whether <paramref name="roles"/> grant <c>start</c> on <paramref name="process"/>.</summary>
    public static bool AllowsStart(ApplicationModel application, IEnumerable<string> roles, ProcessModel process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return Grants(application, roles).Any(grant => grant.Process?.Id == process.Id && grant.Actions.Contains(PolicyAction.Start));
    }

    /// <summary>Whether <paramref name="roles"/> grant <c>inspect</c> on <paramref name="process"/>.</summary>
    public static bool AllowsInspect(ApplicationModel application, IEnumerable<string> roles, ProcessModel process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return Grants(application, roles).Any(grant => grant.Process?.Id == process.Id && grant.Actions.Contains(PolicyAction.Inspect));
    }

    /// <summary>Whether <paramref name="roles"/> grant <c>cancel</c> on <paramref name="process"/>.</summary>
    public static bool AllowsCancel(ApplicationModel application, IEnumerable<string> roles, ProcessModel process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return Grants(application, roles).Any(grant => grant.Process?.Id == process.Id && grant.Actions.Contains(PolicyAction.Cancel));
    }

    /// <summary>Whether <paramref name="roles"/> grant <c>retry</c> on <paramref name="process"/>.</summary>
    public static bool AllowsRetry(ApplicationModel application, IEnumerable<string> roles, ProcessModel process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return Grants(application, roles).Any(grant => grant.Process?.Id == process.Id && grant.Actions.Contains(PolicyAction.Retry));
    }

    /// <summary>Whether <paramref name="roles"/> grant <c>resolve</c> on <paramref name="process"/>.</summary>
    public static bool AllowsResolve(ApplicationModel application, IEnumerable<string> roles, ProcessModel process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return Grants(application, roles).Any(grant => grant.Process?.Id == process.Id && grant.Actions.Contains(PolicyAction.Resolve));
    }

    /// <summary>Whether <paramref name="roles"/> grant <c>reassign</c> on <paramref name="process"/>.</summary>
    public static bool AllowsReassign(ApplicationModel application, IEnumerable<string> roles, ProcessModel process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return Grants(application, roles).Any(grant => grant.Process?.Id == process.Id && grant.Actions.Contains(PolicyAction.Reassign));
    }

    /// <summary>Whether <paramref name="roles"/> grant <c>reassign</c> on some process of <paramref name="application"/>.</summary>
    public static bool AllowsReassignAny(ApplicationModel application, IEnumerable<string> roles)
    {
        return Grants(application, roles).Any(grant => grant.Process is not null && grant.Actions.Contains(PolicyAction.Reassign));
    }

    /// <summary>Whether <paramref name="roles"/> grant <c>invoke</c> on <paramref name="endpoint"/>.</summary>
    public static bool AllowsInvoke(ApplicationModel application, IEnumerable<string> roles, EndpointModel endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return Grants(application, roles).Any(grant => grant.Endpoint?.Id == endpoint.Id && grant.Actions.Contains(PolicyAction.Invoke));
    }

    /// <summary>
    /// The claim values in <paramref name="roles"/> that name a role <paramref name="application"/>
    /// declares, compared exactly. Any other value is not a role of the application.
    /// </summary>
    public static IReadOnlyList<string> HeldRoles(ApplicationModel application, IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(roles);

        var declared = application.Roles.Select(role => role.Name).ToHashSet(StringComparer.Ordinal);
        return [.. roles.Where(declared.Contains).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The grants of every policy that names a role the user holds.</summary>
    private static IEnumerable<GrantModel> Grants(ApplicationModel application, IEnumerable<string> roles) =>
        Policies(application, roles).SelectMany(policy => policy.Grants);

    /// <summary>Every policy that names a role the user holds, in declaration order.</summary>
    private static IEnumerable<PolicyModel> Policies(ApplicationModel application, IEnumerable<string> roles)
    {
        // A claim value counts only when it names a declared role, compared exactly.
        var held = HeldRoles(application, roles).ToHashSet(StringComparer.Ordinal);
        return application.Policies.Where(policy => policy.Roles.Any(held.Contains));
    }
}

/// <summary>
/// A record filter of one grant: the policy's name, the grant's index in the policy's grants, and
/// its filter. The name and the index identify the grant in a log line.
/// </summary>
public sealed record RecordFilter(string Policy, int Grant, ExpressionModel Expression);

/// <summary>
/// What a user may reach for one action on one entity. <see cref="Granted"/> is whether any grant
/// allows the action. <see cref="Filters"/> is null when a grant reaches every record. Otherwise a
/// record is reached when it matches any of them, and an empty list reaches none.
/// </summary>
public sealed record RecordAccess(bool Granted, IReadOnlyList<RecordFilter>? Filters);
