using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// The grant ceiling of a module contract, as an application sees it. A role gets the entries of every
/// slot of the module that the application maps to it, also through a chain of module dependencies. A
/// role mapped to no slot gets <c>others</c>, and with no <c>others</c> it gets no entry. A grant on a
/// module entity that has a ceiling is checked against its role's entries: for each action some entry
/// lists the action, and the grant's fields are within the union of those entries' fields. A grant with
/// no <c>fields</c> list is outside entries that all have one. Application policies and a module's own
/// policies are both checked, and a module policy's slot is judged as the application role it maps to.
/// </summary>
internal static class GrantCeilings
{
    private static readonly IReadOnlyList<string> _entityActions = PolicyActions.Of("entity");

    /// <summary>
    /// The ceiling entries of <paramref name="role"/>. <paramref name="slotRoles"/> maps each slot of the
    /// module to the application role it reaches. A mapped slot without a key in the ceiling adds nothing.
    /// </summary>
    public static IReadOnlyList<CeilingEntryDefinition> EntriesFor(
        ContractCeilingDefinition ceiling,
        IReadOnlyDictionary<string, string> slotRoles,
        string role)
    {
        var mapped = SlotsOf(slotRoles, role);
        return mapped.Count == 0
            ? ceiling.Others ?? []
            : [.. mapped.SelectMany(slot => ceiling.Slots.GetValueOrDefault(slot) ?? [])];
    }

    /// <summary>The slots of the module that the application maps to <paramref name="role"/>, in slot name order.</summary>
    public static IReadOnlyList<string> SlotsOf(IReadOnlyDictionary<string, string> slotRoles, string role) =>
        [.. slotRoles.Where(pair => string.Equals(pair.Value, role, StringComparison.Ordinal)).Select(pair => pair.Key).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Reports each grant of <paramref name="policies"/> that goes past the ceiling of its role, once for
    /// each role, at the grant. <paramref name="contracts"/> holds the checked contracts by the qualified
    /// name of their entity, and <paramref name="pins"/> the pinned modules with their role mapping.
    /// </summary>
    public static void Check(
        IReadOnlyList<PolicyResource> policies,
        IReadOnlyDictionary<string, ContractResource> contracts,
        IReadOnlyList<PinnedModuleModel> pins,
        Func<string, EntityResource?> findEntity,
        Func<string, RoleResource?> findRole,
        List<Diagnostic> diagnostics)
    {
        foreach (var policy in policies)
        {
            for (var index = 0; index < policy.Grants.Count; index++)
            {
                var grant = policy.Grants[index];
                if (grant.Entity is not { } name
                    || grant.DataSource is not null
                    || grant.Process is not null
                    || grant.Endpoint is not null
                    || findEntity(name) is not { } entity
                    || !contracts.TryGetValue(entity.Name, out var contract)
                    || contract.Ceiling is not { } ceiling
                    || pins.FirstOrDefault(pin => pin.Id == ApplicationCompiler.ModuleIdOf(entity.Name)) is not { } pin)
                {
                    continue;
                }

                foreach (var role in RolesOf(policy, pins, findRole))
                {
                    var entries = EntriesFor(ceiling, pin.Roles, role);
                    var problems = Problems(grant, entity, entries);
                    if (problems.Count == 0)
                    {
                        continue;
                    }

                    var slots = SlotsOf(pin.Roles, role);
                    var source = slots.Count == 0
                        ? ceiling.Others is null ? "the contract has no 'others' entries" : "the 'others' entries"
                        : $"the entries of {(slots.Count == 1 ? "the slot" : "the slots")} {string.Join(", ", slots.Select(slot => $"'{slot}'"))}";
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.GrantPastCeiling,
                        $"The grant goes past the ceiling of the role '{role}' on '{entity.Name}' ({source} in '{contract.File}'): {string.Join(" ", problems)}",
                        policy.File,
                        $"/grants/{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                        policy.Id));
                }
            }
        }
    }

    /// <summary>The application roles a policy grants to. A slot or role that maps to no declared role is left to its own diagnostic.</summary>
    private static IEnumerable<string> RolesOf(PolicyResource policy, IReadOnlyList<PinnedModuleModel> pins, Func<string, RoleResource?> findRole)
    {
        if (!ApplicationCompiler.IsModuleResource(policy))
        {
            return policy.Roles.Select(role => findRole(role)?.Name).OfType<string>().Distinct(StringComparer.Ordinal);
        }

        var pin = pins.FirstOrDefault(candidate => candidate.Id == ApplicationCompiler.ModuleIdOf(policy.Name));
        return pin is null
            ? []
            : policy.Roles.Where(pin.Roles.ContainsKey).Select(slot => pin.Roles[slot]).Distinct(StringComparer.Ordinal);
    }

    /// <summary>Says what in <paramref name="grant"/> is past <paramref name="entries"/>, one sentence for each action. Empty when it is within.</summary>
    private static List<string> Problems(GrantDefinition grant, EntityResource entity, IReadOnlyList<CeilingEntryDefinition> entries)
    {
        var problems = new List<string>();
        // A field the entity lacks has its own diagnostic.
        var grantFields = grant.Fields?
            .Select(name => ApplicationCompiler.FindField(entity, name)?.Name)
            .OfType<string>()
            .ToList();
        foreach (var action in grant.Actions.Distinct(StringComparer.Ordinal).Where(_entityActions.Contains))
        {
            var listing = entries.Where(entry => entry.Actions.Contains(action, StringComparer.Ordinal)).ToList();
            if (listing.Count == 0)
            {
                problems.Add($"No entry lists the action '{action}'.");
            }
            else if (listing.All(entry => entry.Fields is not null))
            {
                var allowed = listing.SelectMany(entry => entry.Fields!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (grantFields is null)
                {
                    problems.Add($"The grant covers every field for '{action}', but the entries that list it allow only {string.Join(", ", allowed.Order(StringComparer.Ordinal).Select(field => $"'{field}'"))}.");
                }
                else if (grantFields.Where(field => !allowed.Contains(field)).ToList() is { Count: > 0 } past)
                {
                    problems.Add($"For '{action}', the fields {string.Join(", ", past.Select(field => $"'{field}'"))} are outside the fields the entries allow.");
                }
            }
        }

        return problems;
    }
}
