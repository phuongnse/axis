using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;

namespace Axis.Api.Infrastructure;

public static class ExternalAuthenticationExtensions
{
    public const string ExternalRegistrationScheme = "ExternalRegistration";

    public static AuthenticationBuilder AddExternalRegistrationProviders(
        this AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        builder.AddCookie(ExternalRegistrationScheme, opts =>
        {
            opts.ExpireTimeSpan = TimeSpan.FromMinutes(15);
            opts.SlidingExpiration = false;
        });

        IConfigurationSection providers = configuration.GetSection("Authentication:ExternalProviders");

        AddMicrosoftIfConfigured(builder, providers.GetSection("Microsoft"));
        AddGoogleIfConfigured(builder, providers.GetSection("Google"));
        AddGitHubIfConfigured(builder, providers.GetSection("GitHub"));

        return builder;
    }

    private static void AddMicrosoftIfConfigured(
        AuthenticationBuilder builder,
        IConfigurationSection section)
    {
        string? clientId = section["ClientId"];
        string? clientSecret = section["ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId))
            return;

        builder.AddMicrosoftAccount(options =>
        {
            options.SignInScheme = ExternalRegistrationScheme;
            options.ClientId = clientId;
            options.ClientSecret = clientSecret ?? string.Empty;
            options.CallbackPath = "/signin-microsoft";
            options.Scope.Add("openid");
            options.Scope.Add("email");
            options.Scope.Add("profile");
        });
    }

    private static void AddGoogleIfConfigured(
        AuthenticationBuilder builder,
        IConfigurationSection section)
    {
        string? clientId = section["ClientId"];
        string? clientSecret = section["ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId))
            return;

        builder.AddGoogle(options =>
        {
            options.SignInScheme = ExternalRegistrationScheme;
            options.ClientId = clientId;
            options.ClientSecret = clientSecret ?? string.Empty;
            options.CallbackPath = "/signin-google";
            options.Scope.Add("email");
            options.Scope.Add("profile");
            // Google does not map the verified flag by default; surface it so the
            // claims extractor can reject unverified provider emails (fail closed).
            options.ClaimActions.MapJsonKey("email_verified", "email_verified");
        });
    }

    private static void AddGitHubIfConfigured(
        AuthenticationBuilder builder,
        IConfigurationSection section)
    {
        string? clientId = section["ClientId"];
        string? clientSecret = section["ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId))
            return;

        builder.AddGitHub(options =>
        {
            options.SignInScheme = ExternalRegistrationScheme;
            options.ClientId = clientId;
            options.ClientSecret = clientSecret ?? string.Empty;
            options.CallbackPath = "/signin-github";
            options.Scope.Add("user:email");

            // GitHub exposes per-address verified flags only via /user/emails, and the
            // default handler does not surface them. Fetch the primary address and emit an
            // explicit email_verified claim so unverified emails are rejected (fail closed).
            options.Events.OnCreatingTicket = async context =>
            {
                using HttpRequestMessage request = new(HttpMethod.Get, "https://api.github.com/user/emails");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.UserAgent.ParseAdd("Axis");

                using HttpResponseMessage response = await context.Backchannel.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    context.HttpContext.RequestAborted);
                if (!response.IsSuccessStatusCode)
                    return;

                await using Stream stream =
                    await response.Content.ReadAsStreamAsync(context.HttpContext.RequestAborted);
                using JsonDocument emails =
                    await JsonDocument.ParseAsync(stream, cancellationToken: context.HttpContext.RequestAborted);

                foreach (JsonElement entry in emails.RootElement.EnumerateArray())
                {
                    if (!entry.TryGetProperty("primary", out JsonElement primary) || !primary.GetBoolean())
                        continue;

                    bool verified = entry.TryGetProperty("verified", out JsonElement verifiedElement)
                        && verifiedElement.GetBoolean();
                    context.Identity?.AddClaim(new Claim("email_verified", verified ? "true" : "false"));
                    break;
                }
            };
        });
    }
}
