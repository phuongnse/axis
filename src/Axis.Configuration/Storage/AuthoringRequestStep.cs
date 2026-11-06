namespace Axis.Configuration.Storage;

/// <summary>One step of an authoring request's loop, in order. A comment that asks for another plan is a <c>planChange</c> step.</summary>
public sealed class AuthoringRequestStep
{
    public required Guid Id { get; init; }

    public required Guid RequestId { get; init; }

    /// <summary>The position of the step in its request, from 1.</summary>
    public required int Ordinal { get; init; }

    public required string Kind { get; init; }

    public string? InputSummary { get; init; }

    public string? Outcome { get; init; }

    public long Tokens { get; init; }

    /// <summary>The step's model messages as JSON, kept for 30 days, or null.</summary>
    public string? Messages { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }
}
