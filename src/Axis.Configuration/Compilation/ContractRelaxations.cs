using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Builds the relaxed copy of a module contract that an application compiles against: the copy lacks the
/// points the application relaxed. See <c>docs/reference/module-contracts.md#relax-entries</c>.
/// </summary>
internal static class ContractRelaxations
{
    /// <summary>
    /// Gives <paramref name="contract"/> without the points of <paramref name="relaxed"/> that name its entity,
    /// or <paramref name="contract"/> itself when there are none. A point that names no element of the
    /// contract changes nothing. A point of an unknown kind throws.
    /// </summary>
    public static ContractResource Apply(ContractResource contract, IReadOnlySet<string> relaxed)
    {
        var prefix = contract.Entity + "/";
        var kept = relaxed.Where(point => point.StartsWith(prefix, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        if (kept.Count == 0)
        {
            return contract;
        }

        foreach (var point in kept)
        {
            var parts = point[prefix.Length..].Split('/');
            var known = parts[0] switch
            {
                "transitions" or "validations" or "effects" or "ceiling" => parts.Length == 2,
                "states" => parts.Length == 3 && parts[2] is "initial" or "deletable",
                _ => false,
            };
            if (!known)
            {
                throw new InvalidOperationException($"The relaxed point '{point}' has a kind that contract relaxations do not handle.");
            }
        }

        var entity = contract.Entity;
        return contract with
        {
            Lifecycle = contract.Lifecycle is not { } lifecycle
                ? null
                : lifecycle with
                {
                    States =
                    [
                        .. lifecycle.States.Select(state => state with
                        {
                            Initial = kept.Contains($"{entity}/states/{state.Name}/initial") ? true : state.Initial,
                            Deletable = kept.Contains($"{entity}/states/{state.Name}/deletable") ? true : state.Deletable,
                        }),
                    ],
                    Transitions =
                    [
                        .. lifecycle.Transitions.Select(transition =>
                            kept.Contains($"{entity}/transitions/{transition.Name}") ? transition with { Guard = null } : transition),
                    ],
                },
            Validations = contract.Validations is null
                ? null
                : [.. contract.Validations.Where(validation => !kept.Contains($"{entity}/validations/{validation.Name}"))],
            Effects = contract.Effects is null
                ? null
                : [.. contract.Effects.Where(effect => !kept.Contains($"{entity}/effects/{effect.Name}"))],
            Ceiling = contract.Ceiling is not { } ceiling
                ? null
                : ceiling with
                {
                    Slots = ceiling.Slots.ToDictionary(
                        slot => slot.Key,
                        slot => kept.Contains($"{entity}/ceiling/{slot.Key}") ? Unlimited : slot.Value,
                        StringComparer.Ordinal),
                    Others = ceiling.Others is not null && kept.Contains($"{entity}/ceiling/others") ? Unlimited : ceiling.Others,
                },
        };
    }

    /// <summary>The one entry of a relaxed slot: every entity action, no fields list and no filter.</summary>
    private static IReadOnlyList<CeilingEntryDefinition> Unlimited { get; } =
        [new CeilingEntryDefinition { Actions = PolicyActions.Of("entity") }];
}
