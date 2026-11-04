using Axis.Server.Authentication;
using Axis.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Axis.Server.CellApi;

/// <summary>
/// The sign-in of the cell console through the operator realm, with the code flow and PKCE. The console is a confidential
/// client of the realm, <see cref="CellOptions.ConsoleClientId"/> with the secret <c>Cell:ConsoleClientSecret</c>. Without the
/// secret, or without a platform host, the scheme is not registered and no console sign-in is offered. The scheme serves only
/// the console host: a callback on another host is skipped, and a challenge there is refused. After the callback the server
/// keeps no token: it reads the realm roles from the access token of the token response, as the bearer path reads them
/// from the bearer token, and creates a <see cref="CellOperatorSession"/>.
/// </summary>
internal static partial class CellConsoleSignIn
{
    public const string Scheme = "Axis.CellConsoleSignIn";

    public const string CallbackPath = "/api/cell/auth/callback";

    /// <summary>
    /// Where the browser goes when the callback fails. The address holds no reason, as the reason is only in the log.
    /// </summary>
    public const string FailedPath = "/?signIn=failed";

    /// <summary>The setting that holds the secret of the console's client in the operator realm.</summary>
    public const string ClientSecretSetting = "Cell:ConsoleClientSecret";

    [LoggerMessage(LogLevel.Warning, "A cell console sign-in was refused: {Reason}.")]
    private static partial void SignInRefused(ILogger logger, string reason);

    /// <summary>
    /// Registers the OIDC client of the cell console. Without an <paramref name="authority"/>, or without a
    /// <paramref name="clientSecret"/>, nothing is registered.
    /// </summary>
    /// <param name="securePolicy">The policy of the helper cookies, the same as the session cookie's.</param>
    /// <param name="allowHttp">Whether the identity provider may be reached over http, in Development and Testing only.</param>
    public static AuthenticationBuilder AddCellConsoleSignIn(
        this AuthenticationBuilder builder,
        Uri? authority,
        CellOptions cell,
        string? clientSecret,
        CookieSecurePolicy securePolicy,
        bool allowHttp)
    {
        if (authority is null || string.IsNullOrWhiteSpace(clientSecret))
        {
            return builder;
        }

        return builder.AddOpenIdConnect(Scheme, options =>
        {
            options.Authority = authority.AbsoluteUri;
            options.ClientId = cell.ConsoleClientId;
            options.ClientSecret = clientSecret;
            options.RequireHttpsMetadata = !allowHttp;
            options.ResponseType = "code";
            // The identity provider returns with a GET. A cross-site form post would not carry
            // the SameSite=Lax correlation and nonce cookies.
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            // The identity provider must ask an operator whose login there is older than the session's absolute lifetime to log in again.
            options.MaxAge = CellOperatorSession.MaxLifetime;
            // The authorization request goes in the redirect, PKCE included, as a standard tenant client sends it (D9).
            options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
            options.SaveTokens = false;
            options.MapInboundClaims = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.UseTokenLifetime = false;
            options.CallbackPath = CallbackPath;
            // Sign-out goes through POST /api/cell/auth/sign-out, so the handler serves no sign-out paths.
            options.SignedOutCallbackPath = PathString.Empty;
            options.RemoteSignOutPath = PathString.Empty;
            options.SignInScheme = CellOperatorSession.Scheme;
            // The defaults are Always and None, and a None cookie must be Secure, which breaks the flow over http.
            options.CorrelationCookie.SecurePolicy = securePolicy;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SecurePolicy = securePolicy;
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            // Their path is the callback path, so the __Host- prefix, which needs path /, is not possible.
            if (securePolicy == CookieSecurePolicy.Always)
            {
                options.CorrelationCookie.Name = "__Secure-Axis.CellCorrelation.";
                options.NonceCookie.Name = "__Secure-Axis.CellNonce.";
            }

            options.Events.OnMessageReceived = context =>
            {
                if (!CellOperatorSession.IsConsoleHost(context.HttpContext, cell.ConsoleHost))
                {
                    context.SkipHandler();
                }

                return Task.CompletedTask;
            };
            options.Events.OnRedirectToIdentityProvider = context =>
            {
                if (!CellOperatorSession.IsConsoleHost(context.HttpContext, cell.ConsoleHost))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    context.HandleResponse();
                }

                return Task.CompletedTask;
            };
            options.Events.OnTokenValidated = context =>
            {
                void Refuse(string reason)
                {
                    SignInRefused(Logger(context.HttpContext), reason);
                    context.Fail(reason);
                }

                var principal = context.Principal;
                var subject = principal?.FindFirst("sub")?.Value;
                if (string.IsNullOrWhiteSpace(subject))
                {
                    Refuse("The ID token has no subject.");
                    return Task.CompletedTask;
                }

                // MapInboundClaims is false, so the claim keeps the name that the token gives it.
                var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
                if (!SignInRecency.TryRead(principal!.FindFirst("auth_time")?.Value, out var authTime)
                    || now - authTime > CellOperatorSession.MaxLifetime + context.Options.TokenValidationParameters.ClockSkew)
                {
                    Refuse("The ID token's auth_time is missing or older than the session's lifetime.");
                    return Task.CompletedTask;
                }

                // Keycloak puts the realm roles in the access token and not in the ID token.
                IEnumerable<string> roles;
                try
                {
                    roles = CellOperator.RealmRoles(new JsonWebToken(context.TokenEndpointResponse?.AccessToken));
                }
                catch (Exception exception) when (exception is ArgumentException or SecurityTokenException)
                {
                    Refuse("The token response holds no readable access token.");
                    return Task.CompletedTask;
                }

                var name = principal.FindFirst("name")?.Value;
                var preferred = principal.FindFirst("preferred_username")?.Value;
                context.Principal = CellOperator.CreatePrincipal(
                    subject,
                    !string.IsNullOrWhiteSpace(name) ? name : !string.IsNullOrWhiteSpace(preferred) ? preferred : subject,
                    roles);
                return Task.CompletedTask;
            };
            // The cookie handler would renew the session that the request's cookie names. This flag makes the
            // session store refuse that session, so every sign-in gets a new session id.
            options.Events.OnTicketReceived = context =>
            {
                context.HttpContext.Items[CellOperatorSession.SigningInItem] = true;
                return Task.CompletedTask;
            };
            // The browser goes to the shell's sign-in page.
            options.Events.OnRemoteFailure = context =>
            {
                SignInRefused(Logger(context.HttpContext), context.Failure?.GetType().Name ?? "Unknown");
                context.Response.Redirect(FailedPath);
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
    }

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CellConsoleSignIn).FullName!);
}
