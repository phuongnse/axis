using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Packages;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled process: its subject entity or, without a subject, its parameters, its optional start condition, its first step and its
/// steps. Every transition names a step of the process by its declared name, every condition
/// is a boolean expression checked over the subject entity's fields and the named rules, every
/// value an operation sets is checked against its field's type over the same scope, and a task's
/// <c>user</c> assignee is a <c>text</c> expression over that scope too. Every input a connector
/// or package operation call sends is checked against the input's type over that scope, and every
/// output it writes names a field that fits the output's type. Every payload field a raised event carries is checked
/// against the field's type over that scope. A notify step's recipient expressions, placeholder values and
/// page parameters are compiled over that scope too.
/// </summary>
public sealed record ProcessModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The process's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    /// <summary>The subject entity, a root entity, or null for a process without a subject.</summary>
    public EntityReference? Entity { get; init; }

    /// <summary>The typed parameters of a process without a subject. Empty when the process has a subject.</summary>
    public IReadOnlyList<ProcessParameterModel> Parameters { get; init; } = [];

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

/// <summary>A parameter of a process without a subject: its name and scalar type.</summary>
public sealed record ProcessParameterModel(string Name, FieldType Type);

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
/// <see cref="Deadline"/> is when the task expires and where the process goes then, or null when it has none.
/// </summary>
public sealed record TaskStepModel(
    string Name,
    TextReference Label,
    TaskAssigneeModel Assignee,
    FormReference Form,
    TimeSpan? DueIn,
    IReadOnlyList<TaskOutcomeModel> Outcomes,
    TaskDeadlineModel? Deadline) : ProcessStepModel(Name);

/// <summary>
/// The assignee of a task: exactly one of <see cref="User"/>, a <c>text</c> expression over the
/// subject record that gives a user id, and <see cref="Role"/>, a role name.
/// </summary>
public sealed record TaskAssigneeModel(ExpressionModel? User, string? Role);

/// <summary>An outcome of a task: its name, its label and the declared name of the step it leads to.</summary>
public sealed record TaskOutcomeModel(string Name, TextReference Label, string Next);

/// <summary>The deadline of a task: the time from the task's creation until it expires, and the declared name of the step the process then moves to.</summary>
public sealed record TaskDeadlineModel(TimeSpan After, string Next);

/// <summary>
/// A timer: the process waits for <see cref="Wait"/> from the time the step runs, or until the time
/// <see cref="Until"/> gives, then goes on at <see cref="Next"/>. Exactly one of <see cref="Wait"/>
/// and <see cref="Until"/> is set.
/// </summary>
public sealed record TimerStepModel(string Name, TimeSpan? Wait, ExpressionModel? Until, string Next) : ProcessStepModel(Name);

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
/// A <c>createRecord</c> operation step: it creates a record of <see cref="Entity"/> through the
/// record create rules, as <c>system</c>, with the values <see cref="Set"/> gives, then moves to
/// <see cref="Next"/>. The expressions are over the process's subject or parameters.
/// </summary>
public sealed record CreateRecordStepModel(string Name, EntityReference Entity, IReadOnlyList<RecordAssignmentModel> Set, string Next) : ProcessStepModel(Name);

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
/// An input a <c>callConnector</c> or package operation step sends: the input's declared name, and
/// the expression over the subject record that gives its value, checked against the input's type.
/// The inputs of a step are in the operation's declaration order.
/// </summary>
public sealed record ConnectorInputModel(string Input, ExpressionModel Value);

/// <summary>
/// An output a <c>callConnector</c> or package operation step writes: the output's declared name
/// and the declared name of the subject field it is written to. The outputs of a step are in the
/// operation's declaration order.
/// </summary>
public sealed record ConnectorOutputModel(string Output, string Field);

/// <summary>
/// The retry policy of a <c>callConnector</c> or package operation step: <see cref="Attempts"/>
/// counts the first attempt, and <see cref="Backoff"/> is the delay before the second one.
/// </summary>
public sealed record RetryPolicyModel(int Attempts, TimeSpan Backoff);

/// <summary>
/// A package operation step: it runs <see cref="Operation"/> of <see cref="Package"/> at the
/// pinned <see cref="Version"/>, with <see cref="Input"/>, writes <see cref="Output"/> to the
/// subject record, and moves to <see cref="Next"/> when the run succeeds. <see cref="Package"/>
/// is the package id as the application lists it and <see cref="Operation"/> is the manifest's
/// declared name. <see cref="Retry"/> says how often and how soon a failed attempt is tried again.
/// <see cref="Idempotent"/>, <see cref="Timeout"/> and <see cref="Capabilities"/> come from the
/// manifest of that version. <see cref="Capabilities"/> names the <c>url</c> and <c>secret</c>
/// deployment bindings the operation declares.
/// </summary>
public sealed record PackageOperationStepModel(
    string Name,
    string Package,
    string Version,
    string Operation,
    IReadOnlyList<ConnectorInputModel> Input,
    IReadOnlyList<ConnectorOutputModel> Output,
    RetryPolicyModel Retry,
    bool Idempotent,
    TimeSpan Timeout,
    string Next,
    PackageCapabilities Capabilities) : ProcessStepModel(Name);

/// <summary>
/// A <c>raiseEvent</c> operation step: it raises <see cref="Event"/>, by its declared name, about
/// the subject record with the payload <see cref="Fields"/> gives, stores the occurrence, and moves
/// to <see cref="Next"/>.
/// </summary>
public sealed record RaiseEventStepModel(string Name, string Event, IReadOnlyList<EventPayloadModel> Fields, string Next) : ProcessStepModel(Name);

/// <summary>
/// A payload field a <c>raiseEvent</c> step gives: the field's declared name, and the expression
/// over the subject record that gives its value, checked against the field's type. The fields of a
/// step are in the event's declaration order.
/// </summary>
public sealed record EventPayloadModel(string Field, ExpressionModel Value);

/// <summary>
/// A <c>notify</c> operation step: it sends a notification to <see cref="Recipients"/> on each of
/// <see cref="Channels"/>, <c>inApp</c> or <c>email</c>, then moves to <see cref="Next"/>. The
/// notification's subject and body are the texts <see cref="Subject"/> and <see cref="Body"/> name, and
/// each <c>{name}</c> placeholder in them takes the value <see cref="Values"/> gives. The values are
/// in ordinal order of their names. <see cref="Open"/> is the page the notification opens, or null.
/// </summary>
public sealed record NotifyStepModel(
    string Name,
    IReadOnlyList<NotifyRecipientModel> Recipients,
    IReadOnlyList<string> Channels,
    TextReference Subject,
    TextReference Body,
    IReadOnlyList<NotifyValueModel> Values,
    NotifyOpenModel? Open,
    string Next) : ProcessStepModel(Name);

/// <summary>
/// A recipient of a <c>notify</c> step: exactly one of <see cref="User"/>, a <c>text</c> expression
/// over the subject record that gives a user id, <see cref="Role"/>, a role name, and
/// <see cref="Email"/>, a <c>text</c> expression over the subject record that gives an email address.
/// </summary>
public sealed record NotifyRecipientModel(ExpressionModel? User, string? Role, ExpressionModel? Email);

/// <summary>A placeholder value of a <c>notify</c> step: the placeholder's name, and the expression over the subject record that gives its value.</summary>
public sealed record NotifyValueModel(string Name, ExpressionModel Value);

/// <summary>
/// The page a <c>notify</c> step's notification opens, and the parameters it gives, each by the
/// declared name of the page's parameter. The parameters are in the page's declaration order.
/// </summary>
public sealed record NotifyOpenModel(PageReference Page, IReadOnlyList<NotifyParameterModel> Parameters);

/// <summary>A parameter a <c>notify</c> step gives the page it opens: the parameter's declared name, and the expression over the subject record that gives its value, checked against the parameter's type.</summary>
public sealed record NotifyParameterModel(string Parameter, ExpressionModel Value);

/// <summary>
/// A <c>subProcess</c> step: it starts the process <see cref="ProcessId"/>, named <see cref="Process"/>
/// by its declared name, and moves to <see cref="Next"/> when that process ends. <see cref="Subject"/>
/// gives the record the child runs for, a reference to the child's subject entity, or is null when
/// the child has no subject or runs for the parent's own record. <see cref="Parameters"/> are the
/// values of a child without a subject, in the child's declaration order.
/// </summary>
public sealed record SubProcessStepModel(
    string Name,
    Guid ProcessId,
    string Process,
    ExpressionModel? Subject,
    IReadOnlyList<SubProcessParameterModel> Parameters,
    string Next) : ProcessStepModel(Name);

/// <summary>
/// A parameter a <c>subProcess</c> step gives: the child's declared parameter name, and the
/// expression over the parent's scope that gives its value, checked against the parameter's type.
/// </summary>
public sealed record SubProcessParameterModel(string Parameter, ExpressionModel Value);

/// <summary>An end step. An instance that reaches it is completed.</summary>
public sealed record EndStepModel(string Name) : ProcessStepModel(Name);
