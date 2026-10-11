using System.Text.Encodings.Web;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Axis.Server.Tests;

/// <summary>A person a test host can sign in through the test authentication scheme.</summary>
internal sealed record TestIdentity(string Id, string DisplayName, params string[] Roles);

/// <summary>
/// Signs people in to test hosts without an identity provider. Only test hosts register it. A
/// request with the <see cref="Header"/> header carries a user id, and the session scheme forwards
/// to this scheme, which builds the same principal as a session of that user on the request's
/// tenant. So the server reads a test sign-in exactly as it reads a session.
/// </summary>
internal static class TestAuthentication
{
    public const string Scheme = "Axis.Test";

    public const string Header = "X-Axis-Test-User";

    /// <summary>
    /// Registers the test scheme with <paramref name="identities"/>, and forwards the session
    /// scheme to it on every request that carries the header.
    /// </summary>
    public static IWebHostBuilder UseTestAuthentication(this IWebHostBuilder builder, params TestIdentity[] identities) =>
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication()
                .AddScheme<TestAuthenticationOptions, TestAuthenticationHandler>(
                    Scheme,
                    options => options.Identities = identities.ToDictionary(identity => identity.Id, StringComparer.Ordinal));
            services.PostConfigure<CookieAuthenticationOptions>(
                Session.Scheme,
                options => options.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey(Header) ? Scheme : null);
        });

    /// <summary>Signs every request of <paramref name="client"/> in as the user <paramref name="id"/>.</summary>
    public static HttpClient SignedInAs(this HttpClient client, string id)
    {
        client.DefaultRequestHeaders.Remove(Header);
        client.DefaultRequestHeaders.Add(Header, id);
        return client;
    }
}

internal sealed class TestAuthenticationOptions : AuthenticationSchemeOptions
{
    public IReadOnlyDictionary<string, TestIdentity> Identities { get; set; } = new Dictionary<string, TestIdentity>();
}

/// <summary>
/// Reads the user id from the header. No header, or no tenant, is no result. An unknown id fails,
/// so it counts as nobody signed in.
/// </summary>
internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<TestAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITenantContextAccessor tenants)
    : AuthenticationHandler<TestAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TestAuthentication.Header, out var values) || values.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (values.Count != 1 || values[0] is not { } id || !Options.Identities.TryGetValue(id, out var identity))
        {
            return Task.FromResult(AuthenticateResult.Fail("The test user is unknown."));
        }

        if (tenants.Current is not { } tenant)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var principal = Session.CreatePrincipal(tenant.TenantId, identity.Id, identity.DisplayName, identity.Roles);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
