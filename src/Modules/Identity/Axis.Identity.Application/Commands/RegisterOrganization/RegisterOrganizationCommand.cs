using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.RegisterOrganization;

public record RegisterOrganizationCommand(
    string OrgName,
    string OrganizationContactEmail,
    string AcceptedTermsVersion,
    string AcceptedPrivacyVersion,
    Guid? SubscriptionPlanId = null,
    string? IdempotencyKey = null) : ICommand;
