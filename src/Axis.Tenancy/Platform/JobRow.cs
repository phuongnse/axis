namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.jobs</c>: one provisioning job that the publisher runs, such as a tenant suspend. A job is
/// <c>queued</c>, <c>running</c> under a lease, and then <c>succeeded</c> or <c>failed</c>. The <see cref="Claims"/> count
/// goes up on every claim and fences the final update. A job that waits for a tenant lease adds to <see cref="Waits"/>
/// too, and a wait does not count toward the limit of three claims. The table has no foreign key to the tenants, so a job outlives its tenant.
/// </summary>
public sealed class JobRow
{
    public Guid Id { get; set; }

    /// <summary>The kind of job, such as <c>suspend</c>, <c>resume</c> or <c>create</c>.</summary>
    public string Kind { get; set; } = "";

    public string TenantId { get; set; } = "";

    public string State { get; set; } = "queued";

    /// <summary>The steps as a <c>jsonb</c> array of <c>{ name, state, error }</c>, in the order they run.</summary>
    public string Steps { get; set; } = "[]";

    public string? Error { get; set; }

    /// <summary>The job's input as <c>jsonb</c>, such as a create's <c>{ application, administrator }</c>, or null.</summary>
    public string? Input { get; set; }

    public string? IdempotencyKey { get; set; }

    /// <summary>The hex SHA-256 of the request that queued the job.</summary>
    public string? RequestHash { get; set; }

    /// <summary>Who asked for the job, such as an operator or a machine caller.</summary>
    public string Actor { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? ClaimedBy { get; set; }

    /// <summary>When the claim ends if nothing renews it, by the database clock.</summary>
    public DateTimeOffset? LeaseUntil { get; set; }

    public int Claims { get; set; }

    /// <summary>The times the job waited for a tenant lease. They are part of <see cref="Claims"/> and do not count as claims.</summary>
    public int Waits { get; set; }
}
