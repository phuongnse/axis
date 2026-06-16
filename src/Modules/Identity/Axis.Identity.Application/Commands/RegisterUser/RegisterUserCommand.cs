using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.RegisterUser;

public sealed record RegisterUserCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string PasswordConfirmation,
    string AcceptedTermsVersion,
    string AcceptedPrivacyVersion,
    string? OrganizationSetupToken = null,
    string? IdempotencyKey = null) : ICommand;
