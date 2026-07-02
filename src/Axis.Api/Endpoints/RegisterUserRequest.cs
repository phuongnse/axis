namespace Axis.Api.Endpoints;

public sealed record RegisterUserRequest(
    string FullName,
    string Email,
    string Password,
    string PasswordConfirmation,
    string AcceptedTermsVersion,
    string AcceptedPrivacyVersion);
