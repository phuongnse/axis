namespace Axis.ControlPlane.Storage;

/// <summary>
/// A row of <c>control_plane.jobs</c>: one durable job of a kind with a JSON payload. <see cref="State"/> is <c>queued</c>,
/// <c>running</c>, <c>succeeded</c> or <c>failed</c>. <see cref="Attempts"/> counts the claims, not the failures.
/// </summary>
public sealed class JobRow
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = "";

    /// <summary>The JSON text of the payload.</summary>
    public string Payload { get; set; } = "{}";

    public string State { get; set; } = "queued";

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseUntil { get; set; }

    /// <summary>The exception type and message of the last failed attempt. It never holds a payload.</summary>
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}
