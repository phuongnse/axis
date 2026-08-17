using System.ComponentModel.DataAnnotations;

namespace Axis.Solutions.Contracts;

public enum SolutionTrustStatus
{
    Trusted = 0,
    Revoked = 1,
    Unknown = 2,
}

public enum SolutionProvisioningStatus
{
    Installing = 0,
    Installed = 1,
    Failed = 2,
}

public enum SolutionComplianceStatus
{
    Compliant = 0,
    Noncompliant = 1,
}

public enum SolutionOperationStatus
{
    Pending = 0,
    Running = 1,
    Failed = 2,
    Blocked = 3,
    Succeeded = 4,
}

public enum SolutionStepStatus
{
    Pending = 0,
    Applying = 1,
    Confirmed = 2,
    Failed = 3,
}

public sealed record SolutionResourceActorDto(
    [property: Required] string Kind,
    Guid? SubjectId,
    [property: Required] string DisplayName);

public sealed record SolutionResourceMetadataDto(
    long? Revision,
    [property: Required] SolutionResourceActorDto CreatedBy,
    [property: Required] DateTimeOffset CreatedAt,
    [property: Required] SolutionResourceActorDto ModifiedBy,
    [property: Required] DateTimeOffset ModifiedAt);

public sealed record SolutionVersionSummaryDto(
    Guid Id,
    string SolutionKey,
    string SolutionVersion,
    string PackageSha256,
    string AxisOpenApiSha256,
    string PublisherId,
    string PublisherKeyId,
    SolutionTrustStatus TrustStatus,
    string SourceRevision,
    string BuildId,
    DateTimeOffset BuiltAt,
    Uri SourceUri,
    DateTimeOffset PublishedAt,
    IReadOnlyList<SolutionComponentPlanDto> Components,
    [property: Required] SolutionResourceMetadataDto Metadata);

public sealed record SolutionComponentIdentityDto(string Type, string Key);

public sealed record SolutionComponentPlanDto(
    string Type,
    string Key,
    string Sha256,
    IReadOnlyList<SolutionComponentIdentityDto> DependsOn);

public sealed record SolutionComponentStatusDto(
    string Type,
    string Key,
    string Sha256,
    SolutionStepStatus Status,
    string? ProblemCode);

public sealed record SolutionInstallationStatusDto(
    Guid Id,
    Guid WorkspaceId,
    Guid SolutionVersionId,
    Guid? OperationId,
    SolutionOperationStatus? OperationStatus,
    SolutionProvisioningStatus ProvisioningStatus,
    SolutionComplianceStatus ComplianceStatus,
    IReadOnlyList<SolutionComponentStatusDto> Components,
    DateTimeOffset UpdatedAt,
    int Revision,
    [property: Required] SolutionResourceMetadataDto Metadata);

public sealed record SolutionOperationStatusDto(
    Guid Id,
    Guid InstallationId,
    SolutionOperationStatus Status,
    long LeaseEpoch,
    string? ProblemCode,
    IReadOnlyList<SolutionComponentStatusDto> Steps,
    DateTimeOffset UpdatedAt);
