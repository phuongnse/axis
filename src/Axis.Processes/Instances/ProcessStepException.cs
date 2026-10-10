using System.Text.Json.Nodes;

namespace Axis.Processes.Instances;

/// <summary>
/// A step of a process instance failed. It carries what the failure transaction records: the step,
/// the instance revision it ran at, its start time and its input.
/// </summary>
internal sealed class ProcessStepException : Exception
{
    public ProcessStepException(string message, string step, long revision, DateTimeOffset startedAt, JsonObject input, Exception? innerException = null)
        : base(message, innerException)
    {
        Step = step;
        Revision = revision;
        StartedAt = startedAt;
        Input = input;
    }

    /// <summary>The declared name of the step that failed.</summary>
    public string Step { get; }

    /// <summary>The instance revision the step ran at.</summary>
    public long Revision { get; }

    /// <summary>The time the step's transaction started, by the database clock.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>The step's input, as its history records it.</summary>
    public JsonObject Input { get; }
}
