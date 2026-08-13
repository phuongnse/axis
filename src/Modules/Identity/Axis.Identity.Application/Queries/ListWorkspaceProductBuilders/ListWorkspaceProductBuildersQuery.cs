using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.ListWorkspaceProductBuilders;

public sealed record ListWorkspaceProductBuildersQuery(
    Guid ActorUserId,
    Guid WorkspaceId) : IQuery<Result<IReadOnlyList<WorkspaceProductBuilderDto>>>;
