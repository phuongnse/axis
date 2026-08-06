namespace Axis.Api.Endpoints;

public sealed record InviteWorkspaceMemberRequest(string Email, string RequestedRole);

public sealed record ChangeWorkspaceInvitationRequest(int ExpectedRevision);
