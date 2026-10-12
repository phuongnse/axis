using System.Text.Encodings.Web;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Axis.Server.Tests;

/// <summary>
/// Signs endpoint callers in to test hosts without an identity provider. Only test hosts register
/// it. A request with the <see cref="Header"/> header carries a caller id, and the endpoint caller
/// scheme forwards to this scheme, which builds the same principal as a valid bearer token of that
/// caller on the request's tenant. So the server reads a test caller exactly as it reads a token.
/// </summary>
internal static class TestEndpointCallers
{
    public const string Scheme = "Axis.TestCaller";

    public const string Header = "X-Axis-Test-Caller";

    /// <summary>
    /// Registers the test scheme with <paramref name="callers"/>, and forwards the endpoint caller
    /// scheme to it on every request that carries the header.
    /// </summary>
    public static IWebHostBuilder UseTestEndpointCallers(this IWebHostBuilder builder, params TestIdentity[] callers) =>
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication()
                .AddScheme<TestEndpointCallerOptions, TestEndpointCallerHandler>(
                    Scheme,
                    options => options.Callers = callers.ToDictionary(caller => caller.Id, StringComparer.Ordinal));
            services.PostConfigure<PolicySchemeOptions>(EndpointCaller.Scheme, options =>
            {
                var inner = options.ForwardDefaultSelector;
                options.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey(Header) ? Scheme : inner?.Invoke(context);
            });
        });

    /// <summary>Sends every request of <paramref name="client"/> as the endpoint caller <paramref name="id"/>.</summary>
    public static HttpClient CallingAs(this HttpClient client, string id)
    {
        client.DefaultRequestHeaders.Remove(Header);
        client.DefaultRequestHeaders.Add(Header, id);
        return client;
    }
}

internal sealed class TestEndpointCallerOptions : AuthenticationSchemeOptions
{
    public IReadOnlyDictionary<string, TestIdentity> Callers { get; set; } = new Dictionary<string, TestIdentity>();
}

/// <summary>
/// Reads the caller id from the header. No header, or no tenant, is no result. An unknown id fails,
/// so it counts as a token that is not valid.
/// </summary>
internal sealed class TestEndpointCallerHandler(
    IOptionsMonitor<TestEndpointCallerOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITenantContextAccessor tenants)
    : AuthenticationHandler<TestEndpointCallerOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TestEndpointCallers.Header, out var values) || values.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (values.Count != 1 || values[0] is not { } id || !Options.Callers.TryGetValue(id, out var caller))
        {
            return Task.FromResult(AuthenticateResult.Fail("The test caller is unknown."));
        }

        if (tenants.Current is not { } tenant)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var principal = EndpointCaller.CreatePrincipal(tenant.TenantId, caller.Id, caller.DisplayName, caller.Roles);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
