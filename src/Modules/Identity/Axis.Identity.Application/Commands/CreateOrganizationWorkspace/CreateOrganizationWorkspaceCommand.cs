using Axis.Shared.Application.CQRS;
namespace Axis.Identity.Application.Commands.CreateOrganizationWorkspace;

public sealed record CreateOrganizationWorkspaceCommand(Guid UserId, string Name, string IdempotencyKey, string CorrelationId) : ICommand<CreateOrganizationWorkspaceDto>;
public sealed record CreateOrganizationWorkspaceDto(Guid OrganizationId, string OrganizationName, Guid WorkspaceId, string WorkspaceName, string WorkspaceSlug);
