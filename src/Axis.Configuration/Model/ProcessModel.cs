using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled process: its subject entity, its optional start condition, its first step and its
/// steps. Every transition names a step of the process by its declared name, every condition
/// is a boolean expression checked over the subject entity's fields and the named rules, every
/// value an operation sets is checked against its field's type over the same scope, and a task's
/// <c>user</c> assignee is a <c>text</c> expression over that scope too. Every input a connector
/// call sends is checked against the input's type over that scope, and every output it writes
/// names a field that fits the output's type.
/// </summary>
public sealed record ProcessModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The process's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    /// <summary>The subject entity, a root entity.</summary>
    public required EntityReference Entity { get; init; }

    /// <summary>The condition a subject record must meet for a start, or null when every start is allowed.</summary>
    public ProcessStartConditionModel? StartCondition { get; init; }

    /// <summary>The declared name of the first step.</summary>
    public required string Start { get; init; }

    /// <summary>The steps, in declaration order.</summary>
    public required IReadOnlyList<ProcessStepModel> Steps { get; init; }

    /// <summary>Finds a step by name, ignoring letter case.</summary>
    public bool TryGetStep(string name, [NotNullWhen(true)] out ProcessStepModel? step)
    {
        step = Steps.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return step is not null;
    }
}

/// <summary>A process start condition: a boolean expression over the subject record, and the text shown when a start fails it.</summary>
public sealed record ProcessStartConditionModel(ExpressionModel Expression, TextReference Message);

/// <summary>A step of a process.</summary>
public abstract record ProcessStepModel(string Name);

/// <summary>
/// A decision: its branches are evaluated in order and the first one that is true is taken. A
/// <c>when</c> that gives null counts as false. <see cref="Otherwise"/> is taken when no branch is true.
/// </summary>
public sealed record DecisionStepModel(string Name, IReadOnlyList<DecisionBranchModel> Branches, string Otherwise) : ProcessStepModel(Name);

/// <summary>A branch of a decision: its boolean condition and the declared name of the step it leads to.</summary>
public sealed record DecisionBranchModel(ExpressionModel When, string Next);

/// <summary>
/// A human task: a person assigned through <see cref="Assignee"/> completes it on
/// <see cref="Form"/>, shown over the subject record, with one of its <see cref="Outcomes"/>.
/// <see cref="DueIn"/> is the time from the task's creation to its due date, or null when it has none.
/// </summary>
public sealed record TaskStepModel(
    string Name,
    TextReference Label,
    TaskAssigneeModel Assignee,
    FormReference Form,
    TimeSpan? DueIn,
    IReadOnlyList<TaskOutcomeModel> Outcomes) : ProcessStepModel(Name);

/// <summary>
/// The assignee of a task: exactly one of <see cref="User"/>, a <c>text</c> expression over the
/// subject record that gives a user id, and <see cref="Role"/>, a role name.
/// </summary>
public sealed record TaskAssigneeModel(ExpressionModel? User, string? Role);

/// <summary>An outcome of a task: its name, its label and the declared name of the step it leads to.</summary>
public sealed record TaskOutcomeModel(string Name, TextReference Label, string Next);

/// <summary>
/// An <c>updateRecord</c> operation step: it sets each field in
/// <see cref="Set"/> on the subject record through the record update rules, then moves to
/// <see cref="Next"/>.
/// </summary>
public sealed record OperationStepModel(string Name, string Operation, IReadOnlyList<RecordAssignmentModel> Set, string Next) : ProcessStepModel(Name);

/// <summary>
/// A field an <c>updateRecord</c> operation sets: the field's declared name, and the expression
/// over the subject record that gives its value, checked against the field's type.
/// </summary>
public sealed record RecordAssignmentModel(string Field, ExpressionModel Value);

/// <summary>
/// A <c>callConnector</c> operation step: it calls <see cref="Operation"/> of
/// <see cref="Connector"/>, both by their declared names, with <see cref="Input"/>, writes
/// <see cref="Output"/> to the subject record, and moves to <see cref="Next"/> when the call
/// succeeds. <see cref="Retry"/> says how often and how soon a failed attempt is tried again.
/// </summary>
public sealed record CallConnectorStepModel(
    string Name,
    string Connector,
    string Operation,
    IReadOnlyList<ConnectorInputModel> Input,
    IReadOnlyList<ConnectorOutputModel> Output,
    RetryPolicyModel Retry,
    string Next) : ProcessStepModel(Name);

/// <summary>
/// An input a <c>callConnector</c> step sends: the input's declared name, and the expression over
/// the subject record that gives its value, checked against the input's type. The inputs of a step
/// are in the operation's declaration order.
/// </summary>
public sealed record ConnectorInputModel(string Input, ExpressionModel Value);

/// <summary>
/// An output a <c>callConnector</c> step writes: the output's declared name and the declared name
/// of the subject field it is written to. The outputs of a step are in the operation's declaration order.
/// </summary>
public sealed record ConnectorOutputModel(string Output, string Field);

/// <summary>
/// The retry policy of a <c>callConnector</c> step: <see cref="Attempts"/> counts the first
/// attempt, and <see cref="Backoff"/> is the delay before the second one.
/// </summary>
public sealed record RetryPolicyModel(int Attempts, TimeSpan Backoff);

/// <summary>An end step. An instance that reaches it is completed.</summary>
public sealed record EndStepModel(string Name) : ProcessStepModel(Name);
