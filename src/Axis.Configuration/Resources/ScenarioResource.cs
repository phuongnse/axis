namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>scenario</c> resource: a configuration-level test that runs steps against the application
/// as named users. The schema checks its shape and which properties each step type takes. The
/// roles, entities, fields, processes, steps, outcomes, aliases, values and stubs it names are
/// checked by <see cref="Compilation.ApplicationCompiler"/>. A scenario is not part of a release or
/// its content hash.
/// </summary>
public sealed record ScenarioResource : Resource
{
    /// <summary>The time that expressions and task due dates read, an RFC 3339 date-time with an offset, or null for the real clock.</summary>
    public string? Now { get; init; }

    /// <summary>The users of the scenario, each with its role names, in file order. The schema requires at least one.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Users { get; init; }

    /// <summary>
    /// The contract points the scenario tests, or null for a scenario that is no contract scenario. Only a
    /// module's scenario may have it.
    /// </summary>
    public ScenarioContractDefinition? Contract { get; init; }

    /// <summary>The outputs of the connector and package operations the scenario calls, in file order.</summary>
    public IReadOnlyList<ScenarioStubDefinition> Stubs { get; init; } = [];

    /// <summary>The steps, in the order they run. The schema requires at least one.</summary>
    public required IReadOnlyList<ScenarioStepDefinition> Steps { get; init; }
}

/// <summary>The <c>contract</c> of a module scenario as written: the named points it tests.</summary>
public sealed record ScenarioContractDefinition
{
    /// <summary>The named points, each written <c>&lt;entity&gt;/&lt;part&gt;/&lt;name&gt;</c>. The schema requires at least one.</summary>
    public required IReadOnlyList<string> Covers { get; init; }
}
