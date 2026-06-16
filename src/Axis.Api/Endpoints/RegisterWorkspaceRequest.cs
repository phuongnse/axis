namespace Axis.Api.Endpoints;

public record RegisterWorkspaceRequest(
    string WorkspaceName,
    string WorkspaceContactEmail,
    string AcceptedTermsVersion,
    string AcceptedPrivacyVersion,
    Guid? SubscriptionPlanId = null);
