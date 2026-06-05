using Axis.Identity.Domain.Legal;

namespace Axis.Api.Tests.Helpers;

internal static class TestRegistrationPayload
{
    public const string AdminPassword = "TestPass1";

    public static string OrganizationContactEmail(string suffix) =>
        $"contact{suffix}@test.com";

    public static string AdminEmail(string suffix) =>
        $"admin{suffix}@test.com";

    public static object Create(string suffix) => new
    {
        orgName = $"TestOrg{suffix}",
        organizationContactEmail = OrganizationContactEmail(suffix),
        acceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
        acceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
    };

    public static object CreateUser(string suffix, string? organizationSetupToken = null) => new
    {
        firstName = "Test",
        lastName = "Admin",
        email = AdminEmail(suffix),
        password = AdminPassword,
        passwordConfirmation = AdminPassword,
        acceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
        acceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
        organizationSetupToken,
    };
}
