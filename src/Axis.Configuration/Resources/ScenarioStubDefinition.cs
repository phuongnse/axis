using System.Text.Json;

namespace Axis.Configuration.Resources;

/// <summary>
/// The output of one operation as written in a scenario: a connector operation, or a package
/// operation written <c>&lt;package&gt;/&lt;operation&gt;</c>. The schema requires exactly one of
/// <see cref="Connector"/> and <see cref="Operation"/>, and exactly one of <see cref="Output"/> and
/// <see cref="Outcome"/>.
/// </summary>
public sealed record ScenarioStubDefinition
{
    /// <summary>The <see cref="Outcome"/> of a stub whose call fails.</summary>
    public const string Failed = "failed";

    /// <summary>The connector operation the stub answers, or null for a package operation.</summary>
    public ConnectorStepReference? Connector { get; init; }

    /// <summary>The package operation the stub answers, or null for a connector operation.</summary>
    public string? Operation { get; init; }

    /// <summary>The output values, by output field name, in file order. Empty when the stub has an <see cref="Outcome"/>.</summary>
    public IReadOnlyDictionary<string, JsonElement> Output { get; init; } = new Dictionary<string, JsonElement>();

    /// <summary><see cref="Failed"/> when the call fails instead of giving an output, or null.</summary>
    public string? Outcome { get; init; }
}
