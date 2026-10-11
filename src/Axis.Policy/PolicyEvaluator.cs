using Axis.Configuration.Model;

namespace Axis.Policy;

/// <summary>
/// Decides whether a user's roles grant an action on an entity or a data source of an
/// application. Access is denied unless a grant allows it. A role counts only when a roles claim
/// value equals the name of a role the application declares, compared exactly, so any other value
/// grants nothing. A policy applies when the user holds any of its roles. An entity grant with a
/// <see cref="GrantModel.Filter"/> or <see cref="GrantModel.Fields"/> grants nothing until record
/// and field policies are built, so such a grant never opens access.
/// </summary>
public static class PolicyEvaluator
{
    /// <summary>Whether <paramref name="roles"/> grant <paramref name="action"/> on every record of <paramref name="entity"/>.</summary>
    public static bool Allows(ApplicationModel application, IEnumerable<string> roles, EntityModel entity, PolicyAction action) =>
        Grants(application, roles).Any(grant =>
            grant.Entity?.Id == entity.Id
            && grant.Actions.Contains(action)
            && grant.Filter is null
            && grant.Fields is null);

    /// <summary>
    /// Whether <paramref name="roles"/> grant <c>read</c> on <paramref name="dataSource"/>. That
    /// takes a <c>read</c> grant on the data source and one on its root entity.
    /// </summary>
    public static bool AllowsRead(ApplicationModel application, IEnumerable<string> roles, DataSourceModel dataSource)
    {
        var held = roles.ToList();
        return Grants(application, held).Any(grant => grant.DataSource?.Id == dataSource.Id && grant.Actions.Contains(PolicyAction.Read))
            && application.FindEntity(dataSource.Entity.Id) is { } entity
            && Allows(application, held, entity, PolicyAction.Read);
    }

    /// <summary>The grants of every policy that names a role the user holds.</summary>
    private static IEnumerable<GrantModel> Grants(ApplicationModel application, IEnumerable<string> roles)
    {
        // A claim value counts only when it names a declared role, compared exactly.
        var declared = application.Roles.Select(role => role.Name).ToHashSet(StringComparer.Ordinal);
        var held = roles.Where(declared.Contains).ToHashSet(StringComparer.Ordinal);
        return application.Policies
            .Where(policy => policy.Roles.Any(held.Contains))
            .SelectMany(policy => policy.Grants);
    }
}
