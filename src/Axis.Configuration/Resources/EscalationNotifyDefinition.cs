namespace Axis.Configuration.Resources;

/// <summary>The notification of a task escalation as written: the same recipients, channels, message and page as a <c>notify</c> step, without <c>next</c>.</summary>
public sealed record EscalationNotifyDefinition
{
    public required IReadOnlyList<NotifyRecipientDefinition> To { get; init; }

    public required IReadOnlyList<string> Channels { get; init; }

    public required NotifyMessageDefinition Message { get; init; }

    public NotifyOpenDefinition? Open { get; init; }
}
