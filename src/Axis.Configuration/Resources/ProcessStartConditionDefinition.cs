namespace Axis.Configuration.Resources;

/// <summary>A process start condition as written: a boolean expression over the subject record, and the text shown when a start fails it.</summary>
public sealed record ProcessStartConditionDefinition
{
    public required string Expression { get; init; }

    /// <summary>The text whose key is the message of a refused start.</summary>
    public required TextReference Message { get; init; }
}
