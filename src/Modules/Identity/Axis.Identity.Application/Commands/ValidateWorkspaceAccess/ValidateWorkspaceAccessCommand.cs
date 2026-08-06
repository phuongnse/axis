using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
namespace Axis.Identity.Application.Commands.ValidateWorkspaceAccess;

public sealed record ValidateWorkspaceAccessCommand(Guid UserId, Guid WorkspaceId) : ICommand<WorkspaceAccessDto>;
public sealed record WorkspaceAccessDto(Guid WorkspaceId);
