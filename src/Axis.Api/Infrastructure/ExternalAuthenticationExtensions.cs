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
        });
    }
}
