namespace Axis.Solutions.Domain;

public enum SolutionSubjectKind
{
    Human = 0,
    Service = 1,
}

public enum InstallationOperationStatus
{
    Pending = 0,
    Running = 1,
    Failed = 2,
    Blocked = 3,
    Succeeded = 4,
}

public enum InstallationStepStatus
{
    Pending = 0,
    Applying = 1,
    Confirmed = 2,
    Failed = 3,
}

public sealed class SolutionInstallationOperation
{
    private readonly List<SolutionInstallationStep> _steps = [];

    private SolutionInstallationOperation()
    {
    }

    private SolutionInstallationOperation(
        Guid workspaceId,
        Guid actorSubjectId,
        SolutionSubjectKind actorSubjectKind,
        string actorCorrelationId,
        Guid installationId,
        string idempotencyKey,
        string requestHash,
        IEnumerable<SolutionInstallationStep> steps,
        DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        WorkspaceId = workspaceId;
        ActorSubjectId = actorSubjectId;
        ActorSubjectKind = actorSubjectKind;
        ActorCorrelationId = actorCorrelationId;
        InstallationId = installationId;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        Status = InstallationOperationStatus.Pending;
        _steps.AddRange(steps);
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid ActorSubjectId { get; private set; }
    public SolutionSubjectKind ActorSubjectKind { get; private set; }
    public string ActorCorrelationId { get; private set; } = string.Empty;
    public Guid InstallationId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public InstallationOperationStatus Status { get; private set; }
    public long LeaseEpoch { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public string? ProblemCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int Revision { get; private set; }
    public IReadOnlyList<SolutionInstallationStep> Steps => _steps;

    public static SolutionInstallationOperation Create(
        Guid workspaceId,
        Guid actorSubjectId,
        SolutionSubjectKind actorSubjectKind,
        string actorCorrelationId,
        Guid installationId,
        string idempotencyKey,
        string requestHash,
        IReadOnlyList<SolutionComponentPlan> plan,
        DateTimeOffset now)
    {
        if (workspaceId == Guid.Empty || actorSubjectId == Guid.Empty ||
            !Enum.IsDefined(actorSubjectKind) || installationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(actorCorrelationId) || actorCorrelationId.Trim().Length > 120 ||
            now == default || plan.Count == 0)
            throw new ArgumentException("Operation data is incomplete.");
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        if (plan.Select(item => (item.Type, item.Key)).Distinct().Count() != plan.Count)
            throw new ArgumentException("Component plan identities must be unique.", nameof(plan));

        return new SolutionInstallationOperation(
            workspaceId,
            actorSubjectId,
            actorSubjectKind,
            actorCorrelationId.Trim(),
            installationId,
            idempotencyKey,
            requestHash,
            plan.Select((item, index) => SolutionInstallationStep.Create(index, item)),
            now);
    }

    public long AcquireLease(DateTimeOffset now, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || Status is InstallationOperationStatus.Blocked or InstallationOperationStatus.Succeeded)
            throw new InvalidOperationException("Operation cannot be leased.");
        if (Status == InstallationOperationStatus.Running && LeaseExpiresAt > now)
            throw new InvalidOperationException("Operation already has an active lease.");

        ReclaimExpiredApplyingStep(now);
        LeaseEpoch++;
        LeaseExpiresAt = now.Add(duration);
        Status = InstallationOperationStatus.Running;
        Advance(now);
        return LeaseEpoch;
    }

    public SolutionInstallationStep ClaimNext(long leaseEpoch, DateTimeOffset now)
    {
        RequireLease(leaseEpoch, now);
        SolutionInstallationStep step = _steps.FirstOrDefault(item => item.Status == InstallationStepStatus.Pending)
            ?? throw new InvalidOperationException("No pending step exists.");
        step.MarkApplying(leaseEpoch);
        Advance(now);
        return step;
    }

    public void Confirm(Guid stepId, long leaseEpoch, DateTimeOffset now)
    {
        RequireLease(leaseEpoch, now);
        SolutionInstallationStep step = Find(stepId);
        step.Confirm(leaseEpoch);
        if (_steps.All(item => item.Status == InstallationStepStatus.Confirmed))
        {
            Status = InstallationOperationStatus.Succeeded;
            LeaseExpiresAt = null;
        }
        else
        {
            Status = InstallationOperationStatus.Pending;
            LeaseExpiresAt = null;
        }
        Advance(now);
    }

    public void RecordRetryableFailure(Guid stepId, long leaseEpoch, string problemCode, DateTimeOffset now)
    {
        RequireLease(leaseEpoch, now);
        Find(stepId).ReturnPending(leaseEpoch);
        Status = InstallationOperationStatus.Failed;
        ProblemCode = problemCode;
        LeaseExpiresAt = null;
        Advance(now);
    }

    public void Block(Guid stepId, long leaseEpoch, string problemCode, DateTimeOffset now)
    {
        RequireLease(leaseEpoch, now);
        Find(stepId).Fail(leaseEpoch, problemCode);
        Status = InstallationOperationStatus.Blocked;
        ProblemCode = problemCode;
        LeaseExpiresAt = null;
        Advance(now);
    }

    public void BlockBeforeNextMutation(string problemCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(problemCode);
        if (Status == InstallationOperationStatus.Succeeded)
            return;
        if (Status == InstallationOperationStatus.Blocked
            && string.Equals(ProblemCode, problemCode, StringComparison.Ordinal))
            return;

        SolutionInstallationStep? next = _steps.FirstOrDefault(item =>
            item.Status is InstallationStepStatus.Pending or InstallationStepStatus.Applying);
        next?.FailBeforeMutation(problemCode);
        Status = InstallationOperationStatus.Blocked;
        ProblemCode = problemCode;
        LeaseExpiresAt = null;
        Advance(now);
    }

    public void Resume(DateTimeOffset now)
    {
        if (Status != InstallationOperationStatus.Failed)
            throw new InvalidOperationException("Only a retryable failed operation can resume.");
        Status = InstallationOperationStatus.Pending;
        ProblemCode = null;
        Advance(now);
    }

    private void ReclaimExpiredApplyingStep(DateTimeOffset now)
    {
        if (Status != InstallationOperationStatus.Running || LeaseExpiresAt > now)
            return;
        SolutionInstallationStep? applying = _steps.SingleOrDefault(item => item.Status == InstallationStepStatus.Applying);
        applying?.ReclaimExpired();
        Status = InstallationOperationStatus.Pending;
        LeaseExpiresAt = null;
    }

    private void RequireLease(long leaseEpoch, DateTimeOffset now)
    {
        if (Status != InstallationOperationStatus.Running || LeaseEpoch != leaseEpoch || LeaseExpiresAt <= now)
            throw new InvalidOperationException("Lease is stale or expired.");
    }

    private SolutionInstallationStep Find(Guid stepId) =>
        _steps.SingleOrDefault(item => item.Id == stepId)
        ?? throw new KeyNotFoundException("Installation step was not found.");

    private void Advance(DateTimeOffset now)
    {
        if (now < UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(now));
        UpdatedAt = now;
        Revision++;
    }
}

public sealed record SolutionComponentPlan(
    string Type,
    string Key,
    string Sha256,
    IReadOnlyList<SolutionComponentIdentity> DependsOn);

public sealed record SolutionComponentIdentity(string Type, string Key);

public sealed class SolutionInstallationStep
{
    private SolutionInstallationStep()
    {
    }

    private SolutionInstallationStep(int order, SolutionComponentPlan component)
    {
        Id = Guid.NewGuid();
        Order = order;
        Type = component.Type;
        Key = component.Key;
        Sha256 = component.Sha256;
        Status = InstallationStepStatus.Pending;
    }

    public Guid Id { get; private set; }
    public int Order { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string Sha256 { get; private set; } = string.Empty;
    public InstallationStepStatus Status { get; private set; }
    public long ApplyingEpoch { get; private set; }
    public long? ReclaimedEpoch { get; private set; }
    public string? ProblemCode { get; private set; }

    internal static SolutionInstallationStep Create(int order, SolutionComponentPlan component)
    {
        if (order < 0 || string.IsNullOrWhiteSpace(component.Type) || string.IsNullOrWhiteSpace(component.Key) || string.IsNullOrWhiteSpace(component.Sha256))
            throw new ArgumentException("Component plan is invalid.");
        return new SolutionInstallationStep(order, component);
    }

    internal void MarkApplying(long epoch)
    {
        if (Status != InstallationStepStatus.Pending)
            throw new InvalidOperationException("Only a pending step can be claimed.");
        Status = InstallationStepStatus.Applying;
        ApplyingEpoch = epoch;
    }

    internal void Confirm(long epoch)
    {
        RequireEpoch(epoch);
        Status = InstallationStepStatus.Confirmed;
        ProblemCode = null;
    }

    internal void ReturnPending(long epoch)
    {
        RequireEpoch(epoch);
        Status = InstallationStepStatus.Pending;
        ProblemCode = null;
    }

    internal void Fail(long epoch, string problemCode)
    {
        RequireEpoch(epoch);
        Status = InstallationStepStatus.Failed;
        ProblemCode = problemCode;
    }

    internal void FailBeforeMutation(string problemCode)
    {
        if (Status is not (InstallationStepStatus.Pending or InstallationStepStatus.Applying))
            throw new InvalidOperationException("Only an unconfirmed step can be blocked before mutation.");
        Status = InstallationStepStatus.Failed;
        ProblemCode = problemCode;
    }

    internal void ReclaimExpired()
    {
        if (Status != InstallationStepStatus.Applying)
            throw new InvalidOperationException("Only an applying step can be reclaimed.");
        ReclaimedEpoch = ApplyingEpoch;
        Status = InstallationStepStatus.Pending;
    }

    private void RequireEpoch(long epoch)
    {
        if (Status != InstallationStepStatus.Applying || ApplyingEpoch != epoch)
            throw new InvalidOperationException("Step receipt epoch is stale.");
    }
}
