using Axis.Identity.Domain.Legal;

namespace Axis.Api.Tests.Helpers;

internal static class TestRegistrationPayload
{
    public const string AdminPassword = "maple river sunrise";

    public static string TeamContactEmail(string suffix) =>
        $"contact{suffix}@test.com";

    public static string AdminEmail(string suffix) =>
        $"admin{suffix}@test.com";

    public static object Create(string suffix) => new
    {
        teamAccountName = $"TestTeamAccount{suffix}",
        teamContactEmail = TeamContactEmail(suffix),
        acceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
        acceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
    };

    public static object CreateUser(string suffix, string? teamAccountSetupToken = null) => new
    {
        firstName = "Test",
        lastName = "Admin",
        email = AdminEmail(suffix),
        password = AdminPassword,
        passwordConfirmation = AdminPassword,
        acceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
        acceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
        teamAccountSetupToken,
    };
}
