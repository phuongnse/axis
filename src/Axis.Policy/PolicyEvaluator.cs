using Axis.Configuration.Model;

namespace Axis.Policy;

/// <summary>
/// Decides whether a user's roles grant an action on an entity or a data source of an
/// application. Access is denied unless a grant allows it. A role counts only when a roles claim
/// value equals the name of a role the application declares, compared exactly, so any other value
/// grants nothing. A policy applies when the user holds any of its roles. An entity grant with
/// <see cref="GrantModel.Fields"/> grants nothing until field policies are built, so such a grant
/// never opens access. An entity grant with a <see cref="GrantModel.Filter"/> grants the records
/// that match it through <see cref="Access"/> only.
/// </summary>
public static class PolicyEvaluator
{
    /// <summary>
    /// What <paramref name="roles"/> grant for <paramref name="action"/> on
    /// <paramref name="entity"/>. Only grants without <see cref="GrantModel.Fields"/> count. The
    /// access is granted when any such grant exists. It reaches every record when one of them has
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
                if (grant.Entity?.Id != entity.Id || !grant.Actions.Contains(action) || grant.Fields is not null)
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
    /// Whether <paramref name="roles"/> grant <paramref name="action"/> on every record of
    /// <paramref name="entity"/>, through a grant with neither a filter nor fields. The data
    /// source endpoint does not apply record filters yet, so it needs such a grant.
    /// </summary>
    public static bool Allows(ApplicationModel application, IEnumerable<string> roles, EntityModel entity, PolicyAction action) =>
        Grants(application, roles).Any(grant =>
            grant.Entity?.Id == entity.Id
            && grant.Actions.Contains(action)
            && grant.Filter is null
            && grant.Fields is null);

    /// <summary>
    /// Whether <paramref name="roles"/> grant <c>read</c> on <paramref name="dataSource"/>. That
    /// takes a <c>read</c> grant on the data source and one on every record of its root entity, as
    /// <see cref="Allows"/> decides.
    /// </summary>
    public static bool AllowsRead(ApplicationModel application, IEnumerable<string> roles, DataSourceModel dataSource)
    {
        var held = roles.ToList();
        return Grants(application, held).Any(grant => grant.DataSource?.Id == dataSource.Id && grant.Actions.Contains(PolicyAction.Read))
            && application.FindEntity(dataSource.Entity.Id) is { } entity
            && Allows(application, held, entity, PolicyAction.Read);
    }

    /// <summary>The grants of every policy that names a role the user holds.</summary>
    private static IEnumerable<GrantModel> Grants(ApplicationModel application, IEnumerable<string> roles) =>
        Policies(application, roles).SelectMany(policy => policy.Grants);

    /// <summary>Every policy that names a role the user holds, in declaration order.</summary>
    private static IEnumerable<PolicyModel> Policies(ApplicationModel application, IEnumerable<string> roles)
    {
        // A claim value counts only when it names a declared role, compared exactly.
        var declared = application.Roles.Select(role => role.Name).ToHashSet(StringComparer.Ordinal);
        var held = roles.Where(declared.Contains).ToHashSet(StringComparer.Ordinal);
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
