namespace Axis.Configuration.Resources;

/// <summary>A wizard step as written: its title and the names of the sections it shows.</summary>
public sealed record FormStepDefinition(TextReference Title, IReadOnlyList<string> Sections);
