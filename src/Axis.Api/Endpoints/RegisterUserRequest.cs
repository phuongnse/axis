namespace Axis.Api.Endpoints;

public sealed record RegisterUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string PasswordConfirmation,
    string AcceptedTermsVersion,
    string AcceptedPrivacyVersion,
    string? WorkspaceSetupToken = null);
