using Axis.Identity.Domain.Legal;

namespace Axis.Api.Tests.Helpers;

internal static class TestRegistrationPayload
{
    public static object Create(string suffix) => new
    {
        orgName = $"TestOrg{suffix}",
        adminFirstName = "Test",
        adminLastName = "Admin",
        adminEmail = $"admin{suffix}@test.com",
        password = "TestPass1",
        passwordConfirmation = "TestPass1",
        acceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
        acceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
    };
}
