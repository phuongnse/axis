namespace Axis.Identity.Contracts;

public enum WorkspaceProductBuilderDecisionStatus
{
    Denied = 0,
    Allowed = 1,
    Unavailable = 2,
}

public readonly record struct WorkspaceProductBuilderDecision(
    WorkspaceProductBuilderDecisionStatus Status)
{
    public static WorkspaceProductBuilderDecision Denied { get; } =
        new(WorkspaceProductBuilderDecisionStatus.Denied);

    public static WorkspaceProductBuilderDecision Allowed { get; } =
        new(WorkspaceProductBuilderDecisionStatus.Allowed);

    public static WorkspaceProductBuilderDecision Unavailable { get; } =
        new(WorkspaceProductBuilderDecisionStatus.Unavailable);

    public bool IsAllowed => Status == WorkspaceProductBuilderDecisionStatus.Allowed;
    public bool IsUnavailable => Status == WorkspaceProductBuilderDecisionStatus.Unavailable;
}

/// <summary>
/// Resolves the Identity-owned, Workspace-scoped authority to author product definitions.
/// The authenticated subject and Workspace must be server-derived by the caller.
/// </summary>
public interface IWorkspaceProductBuilderAuthorization
{
    Task<WorkspaceProductBuilderDecision> AuthorizeAsync(
        Guid workspaceId,
        SubjectReference subject,
        CancellationToken cancellationToken = default);
}
