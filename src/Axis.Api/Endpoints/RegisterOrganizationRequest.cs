namespace Axis.Api.Endpoints;

public record RegisterOrganizationRequest(
    string OrgName,
    string AdminFirstName,
    string AdminLastName,
    string AdminEmail,
    string Password,
    string PasswordConfirmation,
    Guid? SubscriptionPlanId = null);
