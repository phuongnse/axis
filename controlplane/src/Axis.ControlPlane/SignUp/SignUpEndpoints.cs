namespace Axis.ControlPlane.SignUp;

/// <summary>The public routes of sign-up.</summary>
internal static class SignUpEndpoints
{
    public static void MapSignUpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The answer holds only free and the reason, never a tenant, a cell or a host. Without the directory the check does not exist.
        endpoints.MapGet("/api/sign-up/subdomains/{name}", async (string name, IServiceProvider services, CancellationToken cancellationToken) =>
        {
            var check = services.GetService<SubdomainCheck>();
            if (check is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(await check.CheckAsync(name, cancellationToken));
        });

        // The region's built-in applications from the settings, each with its id, its title and its description in both languages.
        endpoints.MapGet("/api/sign-up/applications", (ControlPlaneOptions options) => Results.Ok(new
        {
            applications = options.SignUp.Applications.Select(application => new
            {
                id = application.Id,
                titles = new { en = application.Titles.En, vi = application.Titles.Vi },
                descriptions = new { en = application.Descriptions.En, vi = application.Descriptions.Vi },
            }),
        }));

        // The region's legal documents, see docs/reference/sign-up.md#the-documents. Without the settings the route cannot answer.
        endpoints.MapGet("/api/sign-up/documents", (ControlPlaneOptions options) =>
        {
            if (!options.SignUp.HasDocuments)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new
            {
                terms = Document(options.SignUp.Terms),
                dpa = Document(options.SignUp.Dpa),
                privacy = Document(options.SignUp.Privacy),
                licencesUrl = options.SignUp.LicencesUrl,
            });
        });

        // The tenant domain that the sign-up page shows after the subdomain, see docs/reference/sign-up.md#the-subdomain.
        endpoints.MapGet("/api/sign-up/region", (ControlPlaneOptions options) => Results.Ok(new { tenantDomain = options.TenantDomain }));
    }

    private static object Document(SignUpDocumentOptions document) => new
    {
        version = document.Version,
        urls = new { en = document.Urls.En, vi = document.Urls.Vi },
    };
}
