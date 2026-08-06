using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
namespace Axis.Identity.Application.Commands.ValidateWorkspaceAccess;

public sealed class ValidateWorkspaceAccessHandler(IWorkspaceRepository workspaces, IWorkspaceMembershipRepository memberships) : ICommandHandler<ValidateWorkspaceAccessCommand, WorkspaceAccessDto>
{ public async Task<Result<WorkspaceAccessDto>> Handle(ValidateWorkspaceAccessCommand command, CancellationToken ct) { Workspace? workspace = await workspaces.GetByIdAsync(command.WorkspaceId, ct); WorkspaceMembership? membership = await memberships.GetActiveAsync(command.WorkspaceId, command.UserId, ct); return workspace?.Status == WorkspaceStatus.Active && membership?.Status == MembershipStatus.Active ? Result.Success(new WorkspaceAccessDto(workspace.Id)) : Result.Failure<WorkspaceAccessDto>(ErrorCodes.NotFound, "Workspace access is unavailable."); } }
