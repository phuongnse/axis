using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetWorkspaceSettings;

public sealed record GetWorkspaceSettingsQuery(Guid workspaceId) : IQuery<WorkspaceSettingsDto?>;
