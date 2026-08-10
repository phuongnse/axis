using Axis.Identity.Contracts;

namespace Axis.Authorization.Contracts;

public enum ProductActionScope { None = 0, Own = 1, All = 2 }
public enum ProductActionKind { NonRecord = 0, Record = 1 }
public enum ProductAuthorizationDecisionStatus { Denied = 0, Allowed = 1, Unavailable = 2 }

public sealed record ProductActionDescriptor(string ActionKey, string ResourceType, ProductActionKind Kind);
public sealed record ProductAuthorizationRequest(
    Guid WorkspaceId,
    SubjectReference Subject,
    string ActionKey,
    string ResourceType,
    string? ResourceKey,
    string CorrelationId);
public sealed record ProductAuthorizationDecision(
    ProductAuthorizationDecisionStatus Status,
    ProductActionScope? Scope)
{
    public ProductAuthorizationDecision(bool isAllowed, ProductActionScope? scope)
        : this(
            isAllowed ? ProductAuthorizationDecisionStatus.Allowed : ProductAuthorizationDecisionStatus.Denied,
            scope)
    {
    }

    public bool IsAllowed => Status == ProductAuthorizationDecisionStatus.Allowed;
    public bool IsUnavailable => Status == ProductAuthorizationDecisionStatus.Unavailable;
    public static ProductAuthorizationDecision Denied { get; } = new(ProductAuthorizationDecisionStatus.Denied, null);
    public static ProductAuthorizationDecision Unavailable { get; } = new(ProductAuthorizationDecisionStatus.Unavailable, null);
}
public sealed record ProductRolePresentation(string DisplayName, string? Description);
public sealed record ProductPolicyRole(string RoleKey, IReadOnlyDictionary<string, ProductRolePresentation> Presentation);
public sealed record ProductPolicyGrant(string RoleKey, string ActionKey, string ResourceType, string? ResourceKey, ProductActionScope Scope);
public sealed record ProductPolicyComponent(string PolicyKey, Guid VersionId, IReadOnlyList<ProductPolicyRole> Roles, IReadOnlyList<ProductPolicyGrant> Grants);
public sealed record ProductRoleAssignment(Guid WorkspaceId, SubjectReference Subject, Guid PolicyVersionId, string RoleKey, bool IsActive, int Revision);

public interface IProductAuthorizationService
{
    Task<ProductAuthorizationDecision> AuthorizeAsync(ProductAuthorizationRequest request, CancellationToken cancellationToken = default);
}

public interface IProductPolicyInstaller
{
    ProductPolicyInstallResult Validate(ProductPolicyComponent component);
    Task<ProductPolicyInstallResult> InstallAsync(InstallProductPolicyRequest request, CancellationToken cancellationToken = default);
    Task<ProductPolicyComponentReadBack?> ReadBackAsync(Guid workspaceId, Guid versionId, CancellationToken cancellationToken = default);
}

public sealed record ProductPolicyInstallResult(bool IsInstalled, string? Error = null);
public sealed record ProductPolicyComponentReadBack(
    Guid WorkspaceId,
    Guid VersionId,
    string SolutionVersion,
    string ComponentHash,
    string OperationId,
    string StepId,
    long LeaseEpoch);
public sealed record InstallProductPolicyRequest(
    Guid WorkspaceId,
    ProductPolicyComponent Component,
    string SolutionVersion,
    string ComponentHash,
    string OperationId,
    string StepId,
    long LeaseEpoch,
    SubjectReference OriginatingSubject,
    string CorrelationId);
