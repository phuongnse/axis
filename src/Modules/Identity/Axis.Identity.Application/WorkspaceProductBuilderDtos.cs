namespace Axis.Identity.Application;

using System.ComponentModel.DataAnnotations;
using Axis.Shared.Application;

public sealed record WorkspaceProductBuilderDto(
    Guid UserId,
    string DisplayName,
    string Email,
    string WorkspaceRole,
    bool IsProductBuilder,
    int MembershipRevision,
    bool CanChange,
    [property: Required] ResourceMetadataDto Metadata);
