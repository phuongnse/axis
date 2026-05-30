using Axis.Identity.Domain.Legal;

namespace Axis.Api.Endpoints;

public static class LegalEndpoints
{
    public static IEndpointRouteBuilder MapLegalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/legal/versions", () => Results.Ok(new
        {
            termsVersion = WellKnownLegalDocuments.TermsVersion,
            privacyVersion = WellKnownLegalDocuments.PrivacyVersion,
        }))
            .AllowAnonymous()
            .WithName("GetLegalVersions")
            .WithSummary("Current Terms of Service and Privacy Policy versions")
            .WithTags("Identity");

        return app;
    }
}
