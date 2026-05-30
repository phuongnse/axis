using System.Security.Claims;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.CreateExternalRegistrationSession;
using Axis.Identity.Application.Queries.GetExternalRegistrationSession;
using Axis.Identity.Application.Queries.GetEnabledExternalProviders;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using AspNet.Security.OAuth.GitHub;
using Microsoft.Extensions.Configuration;

namespace Axis.Api.Endpoints;

public static class ExternalRegistrationEndpoints
{
    private const string CallbackPath = "/connect/external/register/callback";

    public static IEndpointRouteBuilder MapExternalRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/connect/external/register/{provider}", StartExternalRegistration)
            .AllowAnonymous()
            .WithName("ExternalRegistrationChallenge")
            .WithSummary("Start external provider registration OAuth flow")
            .WithTags("Identity")
            .ExcludeFromDescription();

        app.MapGet(CallbackPath, CompleteExternalRegistrationCallback)
            .AllowAnonymous()
            .WithName("ExternalRegistrationCallback")
            .WithSummary("OAuth callback for external provider registration")
            .WithTags("Identity")
            .ExcludeFromDescription();

        RouteGroupBuilder authGroup = app.MapGroup("/api/auth");

        authGroup.MapGet("/external-providers", GetExternalProviders)
            .AllowAnonymous()
            .WithName("GetExternalProviders")
            .WithSummary("List configured external identity providers for registration")
            .WithTags("Identity")
            .Produces<IReadOnlyList<string>>();

        authGroup.MapGet("/external-registration/{sessionId:guid}", GetExternalRegistrationSession)
            .AllowAnonymous()
            .WithName("GetExternalRegistrationSession")
            .WithSummary("Load external registration session details for the completion form")
            .WithTags("Identity")
            .Produces<ExternalRegistrationSessionDto>()
            .Produces(404);

        return app;
    }

    private static IResult StartExternalRegistration(
        string provider,
        IExternalAuthProviderRegistry registry,
        HttpContext httpContext)
    {
        if (!registry.IsProviderEnabled(provider) || !TryGetAuthenticationScheme(provider, out string? scheme))
            return Results.NotFound();

        AuthenticationProperties properties = new()
        {
            RedirectUri = CallbackPath,
            Items =
            {
                ["provider"] = provider.Trim().ToLowerInvariant(),
            },
        };

        return Results.Challenge(properties, [scheme]);
    }

    private static async Task<IResult> CompleteExternalRegistrationCallback(
        HttpContext httpContext,
        ISender mediator,
        IConfiguration configuration,
        CancellationToken ct)
    {
        string? providerName = httpContext.Request.Query["provider"].FirstOrDefault();
        AuthenticateResult? authResult = null;
        string? scheme = null;

        foreach (string candidateScheme in GetCandidateSchemes(providerName))
        {
            AuthenticateResult attempt = await httpContext.AuthenticateAsync(candidateScheme);
            if (attempt.Succeeded)
            {
                authResult = attempt;
                scheme = candidateScheme;
                providerName ??= attempt.Properties?.Items.TryGetValue("provider", out string? storedProvider) == true
                    ? storedProvider
                    : SchemeToProvider(candidateScheme);
                break;
            }
        }

        // OAuth handlers sign in via the external registration cookie after provider callback.
        if (authResult is null)
        {
            AuthenticateResult externalCookie = await httpContext.AuthenticateAsync(
                ExternalAuthenticationExtensions.ExternalRegistrationScheme);
            if (externalCookie.Succeeded)
            {
                authResult = externalCookie;
                scheme = ExternalAuthenticationExtensions.ExternalRegistrationScheme;
                providerName ??= externalCookie.Properties?.Items.TryGetValue("provider", out string? storedProvider) == true
                    ? storedProvider
                    : null;
            }
        }

        string frontendBaseUrl = ResolveFrontendBaseUrl(configuration);

        if (authResult is null || !authResult.Succeeded || providerName is null)
        {
            await SignOutExternalSchemesAsync(httpContext);
            return Results.Redirect(BuildFrontendErrorUrl(frontendBaseUrl, "provider_failed"));
        }

        if (!ExternalAuthProviderRegistry.TryParseProvider(providerName, out ExternalIdentityProvider provider))
        {
            await SignOutExternalSchemesAsync(httpContext);
            return Results.Redirect(BuildFrontendErrorUrl(frontendBaseUrl, "provider_failed"));
        }

        ExternalAuthClaims? claims = ExternalAuthClaimsExtractor.Extract(authResult.Principal!, provider);
        if (claims is null || !claims.HasVerifiedEmail)
        {
            await SignOutExternalSchemesAsync(httpContext, scheme);
            return Results.Redirect(BuildFrontendErrorUrl(frontendBaseUrl, "no_verified_email"));
        }

        Result<Guid> sessionResult = await mediator.Send(
            new CreateExternalRegistrationSessionCommand(
                provider,
                claims.ProviderKey,
                claims.Email,
                claims.DisplayName),
            ct);

        await SignOutExternalSchemesAsync(httpContext, scheme);

        if (sessionResult.IsFailure)
        {
            string errorCode = sessionResult.ErrorCode == ErrorCodes.Conflict
                ? "account_exists"
                : "provider_failed";
            return Results.Redirect(BuildFrontendErrorUrl(frontendBaseUrl, errorCode));
        }

        return Results.Redirect($"{frontendBaseUrl}/register/complete?session={sessionResult.Value}");
    }

    private static async Task<IResult> GetExternalProviders(ISender mediator, CancellationToken ct)
    {
        IReadOnlyList<string> providers =
            await mediator.Send(new GetEnabledExternalProvidersQuery(), ct);
        return Results.Ok(new { providers });
    }

    private static async Task<IResult> GetExternalRegistrationSession(
        Guid sessionId,
        ISender mediator,
        CancellationToken ct)
    {
        ExternalRegistrationSessionDto? session =
            await mediator.Send(new GetExternalRegistrationSessionQuery(sessionId), ct);
        if (session is null)
            return Results.NotFound();

        return Results.Ok(session);
    }

    private static bool TryGetAuthenticationScheme(string provider, out string scheme)
    {
        scheme = provider.Trim().ToLowerInvariant() switch
        {
            "microsoft" => MicrosoftAccountDefaults.AuthenticationScheme,
            "google" => GoogleDefaults.AuthenticationScheme,
            "github" => GitHubAuthenticationDefaults.AuthenticationScheme,
            _ => string.Empty,
        };

        return !string.IsNullOrEmpty(scheme);
    }

    private static IEnumerable<string> GetCandidateSchemes(string? providerName)
    {
        if (providerName is not null && TryGetAuthenticationScheme(providerName, out string scheme))
            yield return scheme;

        yield return MicrosoftAccountDefaults.AuthenticationScheme;
        yield return GoogleDefaults.AuthenticationScheme;
        yield return GitHubAuthenticationDefaults.AuthenticationScheme;
    }

    private static string SchemeToProvider(string scheme) => scheme switch
    {
        var s when s == MicrosoftAccountDefaults.AuthenticationScheme => "microsoft",
        var s when s == GoogleDefaults.AuthenticationScheme => "google",
        var s when s == GitHubAuthenticationDefaults.AuthenticationScheme => "github",
        _ => "unknown",
    };

    private static string ResolveFrontendBaseUrl(IConfiguration configuration)
    {
        string? configured = configuration["Frontend:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');

        string[] allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        return allowedOrigins.FirstOrDefault()?.TrimEnd('/') ?? "http://localhost:5173";
    }

    private static string BuildFrontendErrorUrl(string frontendBaseUrl, string errorCode) =>
        $"{frontendBaseUrl}/register?error={Uri.EscapeDataString(errorCode)}";

    private static async Task SignOutExternalSchemesAsync(HttpContext httpContext, string? primaryScheme = null)
    {
        if (primaryScheme is not null)
            await httpContext.SignOutAsync(primaryScheme);

        await httpContext.SignOutAsync(ExternalAuthenticationExtensions.ExternalRegistrationScheme);
        await httpContext.SignOutAsync(MicrosoftAccountDefaults.AuthenticationScheme);
        await httpContext.SignOutAsync(GoogleDefaults.AuthenticationScheme);
        await httpContext.SignOutAsync(GitHubAuthenticationDefaults.AuthenticationScheme);
    }
}

internal sealed record ExternalAuthClaims(
    string ProviderKey,
    string Email,
    string DisplayName,
    bool HasVerifiedEmail);

internal static class ExternalAuthClaimsExtractor
{
    public static ExternalAuthClaims? Extract(ClaimsPrincipal principal, ExternalIdentityProvider provider)
    {
        string? providerKey = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        string? email = principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email");
        string? displayName = principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("name")
            ?? BuildDisplayName(principal);

        if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(email))
            return null;

        bool verified = provider switch
        {
            ExternalIdentityProvider.Google =>
                string.Equals(
                    principal.FindFirstValue("email_verified"),
                    "true",
                    StringComparison.OrdinalIgnoreCase),
            ExternalIdentityProvider.Microsoft => true,
            ExternalIdentityProvider.GitHub =>
                string.Equals(
                    principal.FindFirstValue("email_verified"),
                    "true",
                    StringComparison.OrdinalIgnoreCase)
                || principal.FindFirst("email")?.Properties.ContainsKey("verified") != true,
            _ => false,
        };

        if (provider == ExternalIdentityProvider.GitHub && email.Contains('@'))
            verified = true;

        return new ExternalAuthClaims(
            providerKey,
            email.Trim(),
            string.IsNullOrWhiteSpace(displayName) ? email.Trim() : displayName.Trim(),
            verified);
    }

    private static string BuildDisplayName(ClaimsPrincipal principal)
    {
        string? givenName = principal.FindFirstValue(ClaimTypes.GivenName);
        string? surname = principal.FindFirstValue(ClaimTypes.Surname);
        if (!string.IsNullOrWhiteSpace(givenName) && !string.IsNullOrWhiteSpace(surname))
            return $"{givenName} {surname}".Trim();

        return principal.Identity?.Name ?? string.Empty;
    }
}
