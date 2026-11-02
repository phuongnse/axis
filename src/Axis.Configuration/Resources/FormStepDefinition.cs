namespace Axis.Configuration.Resources;

/// <summary>A wizard step as written: its name, its title and the names of the sections it shows.</summary>
public sealed record FormStepDefinition(string Name, TextReference Title, IReadOnlyList<string> Sections);
