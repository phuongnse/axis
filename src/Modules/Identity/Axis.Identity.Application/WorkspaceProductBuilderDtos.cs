namespace Axis.Identity.Application;

public sealed record WorkspaceProductBuilderDto(
    Guid UserId,
    string DisplayName,
    string Email,
    string WorkspaceRole,
    bool IsProductBuilder,
    int MembershipRevision,
    bool CanChange);
