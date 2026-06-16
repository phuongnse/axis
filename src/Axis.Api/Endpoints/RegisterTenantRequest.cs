namespace Axis.Api.Endpoints;

public record RegisterTenantRequest(
    string TenantName,
    string TenantContactEmail,
    string AcceptedTermsVersion,
    string AcceptedPrivacyVersion,
    Guid? SubscriptionPlanId = null);
