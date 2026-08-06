using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.ListWorkspaceInvitations;

public sealed record ListWorkspaceInvitationsQuery(
    Guid ActorUserId,
    Guid WorkspaceId,
    int Page,
    int PageSize) : IQuery<Result<WorkspaceInvitationPageDto>>;

public sealed record WorkspaceInvitationPageDto(
    IReadOnlyList<WorkspaceInvitationLifecycleDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
