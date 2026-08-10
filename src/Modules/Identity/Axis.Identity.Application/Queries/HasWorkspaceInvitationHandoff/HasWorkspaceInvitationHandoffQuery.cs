using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.HasWorkspaceInvitationHandoff;

public sealed record HasWorkspaceInvitationHandoffQuery(string HandoffHash) : IQuery<bool>;
