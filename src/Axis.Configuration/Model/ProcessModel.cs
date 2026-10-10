using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled process: its subject entity, its optional start condition, its first step and its
/// steps. Every transition names a step of the process by its declared name, and every condition
/// is a boolean expression checked over the subject entity's fields and the named rules.
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

/// <summary>An end step. An instance that reaches it is completed.</summary>
public sealed record EndStepModel(string Name) : ProcessStepModel(Name);
