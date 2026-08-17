namespace Axis.Identity.Application;

using Axis.Shared.Application;

public sealed record WorkspaceProductBuilderDto(
    Guid UserId,
    string DisplayName,
    string Email,
    string WorkspaceRole,
    bool IsProductBuilder,
    int MembershipRevision,
    bool CanChange,
    ResourceMetadataDto Metadata);
