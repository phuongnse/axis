using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.SetWorkspaceProductBuilder;

public sealed record SetWorkspaceProductBuilderCommand(
    Guid ActorUserId,
    Guid WorkspaceId,
    Guid TargetUserId,
    bool Enabled,
    int ExpectedRevision,
    string CorrelationId,
    string ActorDisplayName) : ICommand<WorkspaceProductBuilderDto>;
