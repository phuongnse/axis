using System.Text.Json;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// The named points of a module's contracts, each written <c>&lt;entity&gt;/&lt;part&gt;/&lt;name&gt;</c> with the
/// entity and the names as the contract writes them. A contract scenario covers these points. See
/// <c>docs/reference/module-contracts.md#named-points</c>.
/// </summary>
internal static class ContractPoints
{
    /// <summary>Builds the points of <paramref name="contracts"/> as written, even a contract that has its own diagnostics.</summary>
    public static IReadOnlySet<string> Of(IEnumerable<ContractResource> contracts)
    {
        var points = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in contracts)
        {
            var entity = contract.Entity;
            foreach (var transition in contract.Lifecycle?.Transitions ?? [])
            {
                points.Add($"{entity}/transitions/{transition.Name}");
            }

            foreach (var state in contract.Lifecycle?.States ?? [])
            {
                if (state.Initial != true)
                {
                    points.Add($"{entity}/states/{state.Name}/initial");
                }

                if (state.Deletable != true)
                {
                    points.Add($"{entity}/states/{state.Name}/deletable");
                }
            }

            foreach (var validation in contract.Validations ?? [])
            {
                points.Add($"{entity}/validations/{validation.Name}");
            }

            if (contract.Effects is { ValueKind: JsonValueKind.Array } effects)
            {
                foreach (var item in effects.EnumerateArray())
                {
                    points.Add($"{entity}/effects/{item.GetProperty("name").GetString()}");
                }
            }

            if (contract.Ceiling is { } ceiling)
            {
                foreach (var slot in ceiling.Slots.Keys)
                {
                    points.Add($"{entity}/ceiling/{slot}");
                }

                if (ceiling.Others is not null)
                {
                    points.Add($"{entity}/ceiling/others");
                }
            }
        }

        return points;
    }
}
