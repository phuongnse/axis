namespace Axis.Api.Endpoints;

public record RegisterTeamAccountRequest(
    string TeamAccountName,
    string TeamContactEmail,
    string AcceptedTermsVersion,
    string AcceptedPrivacyVersion,
    Guid? SubscriptionPlanId = null);
