using Axis.Identity.Application.Queries.GetLegalVersions;
using MediatR;

namespace Axis.Api.Endpoints;

public static class LegalEndpoints
{
    public static IEndpointRouteBuilder MapLegalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/legal/versions", GetLegalVersions)
            .AllowAnonymous()
            .WithName("GetLegalVersions")
            .WithSummary("Current Terms of Service and Privacy Policy versions")
            .WithTags("Identity")
            .Produces<object>();

        return app;
    }

    private static async Task<IResult> GetLegalVersions(ISender mediator, CancellationToken ct)
    {
        LegalVersionsDto versions = await mediator.Send(new GetLegalVersionsQuery(), ct);
        return Results.Ok(new
        {
            termsVersion = versions.TermsVersion,
            privacyVersion = versions.PrivacyVersion,
        });
    }
}
