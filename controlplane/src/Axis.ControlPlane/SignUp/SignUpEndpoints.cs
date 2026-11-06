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

        // The region's built-in applications from the settings, each with its id and its title in both languages.
        endpoints.MapGet("/api/sign-up/applications", (ControlPlaneOptions options) => Results.Ok(new
        {
            applications = options.SignUp.Applications.Select(application => new
            {
                id = application.Id,
                titles = new { en = application.Titles.En, vi = application.Titles.Vi },
            }),
        }));
    }
}
