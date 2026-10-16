using System.Text.Json;

namespace Axis.Configuration.Resources;

/// <summary>
/// The output of one operation as written in a scenario: a connector operation, or a package
/// operation written <c>&lt;package&gt;/&lt;operation&gt;</c>. The schema requires exactly one of
/// <see cref="Connector"/> and <see cref="Operation"/>.
/// </summary>
public sealed record ScenarioStubDefinition
{
    /// <summary>The connector operation the stub answers, or null for a package operation.</summary>
    public ConnectorStepReference? Connector { get; init; }

    /// <summary>The package operation the stub answers, or null for a connector operation.</summary>
    public string? Operation { get; init; }

    /// <summary>The output values, by output field name, in file order.</summary>
    public required IReadOnlyDictionary<string, JsonElement> Output { get; init; }
}
