namespace Axis.Api.Endpoints;

public record RegisterOrganizationRequest(
    string OrgName,
    string OrganizationContactEmail,
    string AcceptedTermsVersion,
    string AcceptedPrivacyVersion,
    Guid? SubscriptionPlanId = null);
