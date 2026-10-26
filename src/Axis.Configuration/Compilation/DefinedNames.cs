namespace Axis.Configuration.Compilation;

/// <summary>
/// Every name an application defines, by kind, as the authoring API's names route answers it. A name is written
/// as a reference writes it, so a module entity is <c>&lt;module id&gt;/&lt;name&gt;</c> and a module rule is
/// <c>&lt;module id&gt;:&lt;name&gt;</c>.
/// </summary>
public sealed record DefinedNames(
    IReadOnlyList<string> Locales,
    IReadOnlyList<DefinedEntity> Entities,
    IReadOnlyList<DefinedDataSource> DataSources,
    IReadOnlyList<DefinedProcess> Processes,
    IReadOnlyList<DefinedRule> Rules,
    IReadOnlyList<DefinedConnector> Connectors,
    IReadOnlyList<DefinedEvent> Events,
    IReadOnlyList<DefinedResource> Roles,
    IReadOnlyList<DefinedResource> Sequences,
    IReadOnlyList<DefinedResource> Forms,
    IReadOnlyList<DefinedPage> Pages,
    IReadOnlyList<DefinedText> Texts)
{
    public static DefinedNames Empty { get; } = new([], [], [], [], [], [], [], [], [], [], [], []);
}

/// <summary>An entity and its fields. <paramref name="Module"/> is the module id, or null for the application's own.</summary>
public sealed record DefinedEntity(Guid Id, string Name, string? Module, IReadOnlyList<DefinedField> Fields);

/// <summary>A field as written. <paramref name="Target"/> and <paramref name="Values"/> are null when the field has none.</summary>
public sealed record DefinedField(string Name, string Type, string? Target, IReadOnlyList<string>? Values);

/// <summary>
/// A data source with its parameters and the columns a row of it carries. <paramref name="GroupBy"/> is the group
/// field list of a grouped data source, and null for a plain one.
/// </summary>
public sealed record DefinedDataSource(
    Guid Id,
    string Name,
    string? Module,
    IReadOnlyList<DefinedTyped> Parameters,
    IReadOnlyList<DefinedColumn> Columns,
    IReadOnlyList<string>? GroupBy);

/// <summary>A name with a type: a rule or data source parameter, or an event field.</summary>
public sealed record DefinedTyped(string Name, string Type);

/// <summary>A column of a data source. <paramref name="Type"/> is null when its path does not resolve.</summary>
public sealed record DefinedColumn(string Name, string? Type);

/// <summary>
/// A process and its steps. <paramref name="Parameters"/> are the parameters of a process without a subject, in file
/// order, and empty for a process with a subject.
/// </summary>
public sealed record DefinedProcess(
    Guid Id,
    string Name,
    IReadOnlyList<DefinedTyped> Parameters,
    IReadOnlyList<DefinedStep> Steps);

/// <summary>A process step. <paramref name="Outcomes"/> is empty for a step that is not a task.</summary>
public sealed record DefinedStep(string Name, string Type, IReadOnlyList<string> Outcomes);

public sealed record DefinedRule(
    Guid Id,
    string Name,
    string? Module,
    IReadOnlyList<DefinedTyped> Parameters,
    string ResultType);

public sealed record DefinedConnector(Guid Id, string Name, IReadOnlyList<DefinedOperation> Operations);

public sealed record DefinedOperation(string Name);

public sealed record DefinedEvent(Guid Id, string Name, IReadOnlyList<DefinedTyped> Fields);

/// <summary>A role, sequence or form.</summary>
public sealed record DefinedResource(Guid Id, string Name);

/// <summary>A page and the parameters it declares.</summary>
public sealed record DefinedPage(Guid Id, string Name, IReadOnlyList<DefinedPageParameter> Parameters);

/// <summary>A page parameter: its name, its type and whether a link must give it.</summary>
public sealed record DefinedPageParameter(string Name, string Type, bool Required);

/// <summary>A text key with its text in each locale that has it, and the number of label uses.</summary>
public sealed record DefinedText(string Key, IReadOnlyDictionary<string, string> Texts, int UsedBy);
