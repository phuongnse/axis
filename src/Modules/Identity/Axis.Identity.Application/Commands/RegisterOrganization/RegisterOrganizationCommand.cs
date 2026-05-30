using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.RegisterOrganization;

public record RegisterOrganizationCommand(
    string OrgName,
    string AdminFirstName,
    string AdminLastName,
    string AdminEmail,
    string? Password,
    string? PasswordConfirmation,
    string AcceptedTermsVersion,
    string AcceptedPrivacyVersion,
    Guid? SubscriptionPlanId = null,
    string? IdempotencyKey = null,
    string? ExternalRegistrationSessionId = null) : ICommand;
