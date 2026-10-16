using System.Text.Json;

namespace Axis.Configuration.Resources;

/// <summary>
/// A scenario step as written. The properties a step's <see cref="Type"/> needs, allows and
/// forbids are checked by the JSON Schema. What it names is checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record ScenarioStepDefinition
{
    public const string CreateRecord = "createRecord";
    public const string UpdateRecord = "updateRecord";
    public const string DeleteRecord = "deleteRecord";
    public const string StartProcess = "startProcess";
    public const string CompleteTask = "completeTask";
    public const string ExpectRecord = "expectRecord";
    public const string ExpectInstance = "expectInstance";
    public const string ExpectTask = "expectTask";

    /// <summary>The step type, one of the constants of this record.</summary>
    public required string Type { get; init; }

    /// <summary>The entity a <c>createRecord</c> step creates a record of.</summary>
    public string? Entity { get; init; }

    /// <summary>The alias of the saved record the step works on.</summary>
    public string? Record { get; init; }

    /// <summary>The alias a <c>createRecord</c> step gives the new record.</summary>
    public string? Save { get; init; }

    /// <summary>The scenario user the step runs as.</summary>
    public string? As { get; init; }

    /// <summary>The process a <c>startProcess</c> or <c>expectInstance</c> step names.</summary>
    public string? Process { get; init; }

    /// <summary>The process step a task or instance step names.</summary>
    public string? Step { get; init; }

    /// <summary>The outcome a <c>completeTask</c> step completes the task with.</summary>
    public string? Outcome { get; init; }

    /// <summary>The instance state or task state an <c>expectInstance</c> or <c>expectTask</c> step asserts.</summary>
    public string? State { get; init; }

    /// <summary>Who an <c>expectTask</c> step asserts the task is assigned to: a scenario user or a role.</summary>
    public TaskAssigneeDefinition? Assignee { get; init; }

    /// <summary>The field values the step writes or asserts, by field name, in file order.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Values { get; init; }

    /// <summary>
    /// What an action must answer instead of succeeding: the string <c>denied</c>, <c>notFound</c> or
    /// <c>conflict</c>, or an object with <c>invalid</c>. Null when the action must succeed.
    /// </summary>
    public JsonElement? Expect { get; init; }
}
