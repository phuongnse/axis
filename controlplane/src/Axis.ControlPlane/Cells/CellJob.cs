namespace Axis.ControlPlane.Cells;

/// <summary>A job of a cell's provisioning API. <c>Steps</c> and <c>Error</c> are set when the job is read.</summary>
internal sealed record CellJob(
    Guid Id,
    string Kind,
    string TenantId,
    string State,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CellJobStep>? Steps = null,
    string? Error = null)
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    /// <summary>True when the job is <c>succeeded</c> or <c>failed</c>.</summary>
    public bool Ended => State is Succeeded or Failed;
}

/// <summary>One step of a job. <c>Error</c> is the refusal of a failed step.</summary>
internal sealed record CellJobStep(string Name, string State, string? Error);
