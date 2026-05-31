using Axis.Identity.Domain.Legal;

namespace Axis.Api.Tests.Helpers;

internal static class TestRegistrationPayload
{
    public static object Create(string suffix) => new
    {
        org_name = $"TestOrg{suffix}",
        admin_first_name = "Test",
        admin_last_name = "Admin",
        admin_email = $"admin{suffix}@test.com",
        password = "TestPass1",
        password_confirmation = "TestPass1",
        accepted_terms_version = WellKnownLegalDocuments.TermsVersion,
        accepted_privacy_version = WellKnownLegalDocuments.PrivacyVersion,
    };
}
