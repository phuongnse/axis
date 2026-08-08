using Axis.Audit.Contracts;
using Axis.Solutions.Contracts;
using Axis.Solutions.Domain;

namespace Axis.Solutions.Application;

public interface ISolutionVersionRepository
{
    Task<SolutionVersion?> FindByIdentityAsync(string solutionKey, string version, CancellationToken cancellationToken = default);
    Task<SolutionVersion?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SolutionVersion>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SolutionVersion>>([]);
    Task AddAsync(SolutionVersion version, IReadOnlyList<VerifiedSolutionComponent> components, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VerifiedSolutionComponent>> GetComponentsAsync(Guid versionId, CancellationToken cancellationToken = default);
    async Task<IReadOnlyDictionary<Guid, IReadOnlyList<VerifiedSolutionComponent>>> GetComponentsAsync(
        IReadOnlyCollection<Guid> versionIds,
        CancellationToken cancellationToken = default)
    {
        Dictionary<Guid, IReadOnlyList<VerifiedSolutionComponent>> result = [];
        foreach (Guid versionId in versionIds)
            result[versionId] = await GetComponentsAsync(versionId, cancellationToken);
        return result;
    }
}

public interface ISolutionInstallationRepository
{
    Task<SolutionInstallation?> FindBySolutionKeyAsync(Guid workspaceId, string solutionKey, CancellationToken cancellationToken = default);
    Task<SolutionInstallation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SolutionInstallation>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task AddAsync(SolutionInstallation installation, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SolutionInstallation>> ListByPublisherKeyAsync(string publisherId, string keyId, CancellationToken cancellationToken = default);
}

public interface ICurrentAxisOpenApiDigestProvider
{
    string? CurrentSha256 { get; }
}

public interface ISolutionOperationRepository
{
    Task<SolutionInstallationOperation?> FindByIdempotencyAsync(Guid workspaceId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<SolutionInstallationOperation?> FindByIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SolutionInstallationOperation>> ListByInstallationAsync(Guid installationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SolutionInstallationOperation>> ListTrackedByInstallationAsync(Guid installationId, CancellationToken cancellationToken = default) =>
        ListByInstallationAsync(installationId, cancellationToken);
    Task<IReadOnlyList<Guid>> ListRunnableIdsAsync(DateTimeOffset now, int maximumCount, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);
    Task AddAsync(SolutionInstallationOperation operation, CancellationToken cancellationToken = default);
}

public interface ISolutionsUnitOfWork
{
    Task BeginAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task AcquirePublisherFenceAsync(string publisherId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class SolutionPersistenceException(string problemCode, Exception innerException) : Exception(problemCode, innerException)
{
    public string ProblemCode { get; } = problemCode;
}

public sealed record SolutionActor(
    Guid SubjectId,
    Guid WorkspaceId,
    string CorrelationId,
    SolutionSubjectKind SubjectKind)
{
    public void Validate()
    {
        if (SubjectId == Guid.Empty || WorkspaceId == Guid.Empty ||
            !Enum.IsDefined(SubjectKind) || string.IsNullOrWhiteSpace(CorrelationId) ||
            CorrelationId.Trim().Length > AuditEventV1Validator.MaximumCorrelationIdLength)
            throw new SolutionPackageException("solutions.actor.invalid");
    }
}

public enum SolutionAuthorityAction { Publish, Install, Resume, Read }

public interface ISolutionAuthority
{
    Task DemandAsync(SolutionActor actor, Guid targetWorkspaceId, SolutionAuthorityAction action, CancellationToken cancellationToken = default);
}

public interface ISolutionsAuditOutbox
{
    Task EnqueueAsync(SolutionAuditEvent auditEvent, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default);
}

public sealed record SolutionAuditEvent(
    Guid EventId,
    AuditActorKindV1 ActorKind,
    Guid? ActorId,
    Guid? SubjectId,
    string CorrelationId,
    SolutionSubjectKind? OriginatingSubjectKind,
    string EventType,
    Guid? WorkspaceId,
    Guid? SolutionVersionId,
    Guid? InstallationId,
    Guid? OperationId,
    string Outcome,
    string? ProblemCode,
    DateTimeOffset OccurredAt);

public sealed record SolutionAdapterPreflight(
    string Type,
    string Key,
    byte[] Content,
    IReadOnlyList<SolutionComponentReference> DependsOn);

public sealed record SolutionApplyReceipt(
    Guid OperationId,
    Guid StepId,
    Guid SolutionVersionId,
    Guid ActorSubjectId,
    SolutionSubjectKind ActorSubjectKind,
    string CorrelationId,
    string SolutionVersion,
    string ComponentSha256,
    long LeaseEpoch);
public sealed record SolutionAdapterReadback(bool IsConfirmed, bool IsMismatch, string? ProblemCode = null);

public sealed class SolutionAdapterException(string problemCode, bool retryable) : Exception(problemCode)
{
    public string ProblemCode { get; } = problemCode;
    public bool Retryable { get; } = retryable;
}

public interface ISolutionComponentAdapter
{
    string ComponentType { get; }
    Task PreflightAsync(Guid workspaceId, SolutionAdapterPreflight component, CancellationToken cancellationToken = default);
    Task<SolutionAdapterReadback> ReadBackAsync(Guid workspaceId, SolutionAdapterPreflight component, SolutionApplyReceipt receipt, CancellationToken cancellationToken = default) => Task.FromResult(new SolutionAdapterReadback(false, false));
    Task ApplyAsync(Guid workspaceId, SolutionAdapterPreflight component, SolutionApplyReceipt receipt, CancellationToken cancellationToken = default);
}

public sealed record PublishSolutionRequest(SolutionActor Actor, byte[] Envelope, DateTimeOffset RequestedAt);
public sealed record InstallSolutionRequest(SolutionActor Actor, Guid WorkspaceId, Guid SolutionVersionId, string IdempotencyKey, string RequestHash, DateTimeOffset RequestedAt);

public sealed record PublishSolutionResult(SolutionVersionSummaryDto Version, bool IsRetry);
public sealed record InstallSolutionResult(SolutionOperationStatusDto Operation, bool IsRetry);
