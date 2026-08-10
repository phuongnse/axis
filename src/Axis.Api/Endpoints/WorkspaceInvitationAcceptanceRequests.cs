namespace Axis.Api.Endpoints;

public sealed record ExchangeWorkspaceInvitationRequest(string Token);

public sealed record WorkspaceInvitationHandoffStateDto(bool Active);

public sealed record WorkspaceInvitationExchangeResponse(string Outcome);
