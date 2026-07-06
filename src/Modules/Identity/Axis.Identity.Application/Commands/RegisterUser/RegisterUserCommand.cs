using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.RegisterUser;

public sealed record RegisterUserCommand(
    string FullName,
    string Email,
    string Password,
    string PasswordConfirmation,
    string AcceptedTermsVersion,
    string AcceptedPrivacyVersion,
    string? PreferredLanguage = null,
    string? IdempotencyKey = null) : ICommand;
