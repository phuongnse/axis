using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.ReviewWorkspaceInvitation;

public sealed record ReviewWorkspaceInvitationQuery(
    string HandoffHash,
    Guid UserId,
    string CorrelationId) : IQuery<Result<WorkspaceInvitationReviewDto>>;
