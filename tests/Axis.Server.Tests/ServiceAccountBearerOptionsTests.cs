using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

/// <summary>
/// A tenant's service account scheme expects <c>Oidc:ServiceAudience</c>. A tenant without it gets no result from the
/// scheme, so no service account token is ever accepted there, and the endpoint audience is no substitute.
/// </summary>
public sealed class ServiceAccountBearerOptionsTests
{
    [Fact]
    public async Task A_tenant_without_a_service_audience_gets_no_result_from_the_service_scheme()
    {
        var options = Configure("service-a", new TenantOidcSettings
        {
            Authority = "https://127.0.0.1:1/realms/axis",
            EndpointAudience = "axis-endpoints",
        });

        var context = await ReceiveAsync(options, "a");

        Assert.True(context.Result?.None);
    }

    [Fact]
    public async Task A_tenant_with_a_service_audience_expects_it_and_lets_the_token_be_checked()
    {
        var options = Configure("service-a", new TenantOidcSettings
        {
            Authority = "https://127.0.0.1:1/realms/axis",
            EndpointAudience = "axis-endpoints",
            ServiceAudience = "axis-services",
        });

        var context = await ReceiveAsync(options, "a");

        Assert.Equal("axis-services", options.Audience);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task The_endpoint_scheme_still_expects_the_endpoint_audience()
    {
        var options = Configure("endpoint-a", new TenantOidcSettings
        {
            Authority = "https://127.0.0.1:1/realms/axis",
            EndpointAudience = "axis-endpoints",
            ServiceAudience = "axis-services",
        });

        var context = await ReceiveAsync(options, "a");

        Assert.Equal("axis-endpoints", options.Audience);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task A_request_of_another_tenant_gets_no_result_from_the_service_scheme()
    {
        var options = Configure("service-a", new TenantOidcSettings
        {
            Authority = "https://127.0.0.1:1/realms/axis",
            ServiceAudience = "axis-services",
        });

        var context = await ReceiveAsync(options, "b");

        Assert.True(context.Result?.None);
    }

    [Theory]
    [InlineData("endpoint-a")]
    [InlineData("service-a")]
    public void Both_bearer_schemes_accept_only_asymmetric_signature_algorithms(string scheme)
    {
        var options = Configure(scheme, new TenantOidcSettings
        {
            Authority = "https://127.0.0.1:1/realms/axis",
            EndpointAudience = "axis-endpoints",
            ServiceAudience = "axis-services",
        });

        var algorithms = options.TokenValidationParameters.ValidAlgorithms.ToArray();

        Assert.Equal(BearerCallerOptions.Algorithms, algorithms);
        Assert.DoesNotContain(algorithms, algorithm => algorithm.StartsWith("HS", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(algorithms, algorithm => string.Equals(algorithm, "none", StringComparison.OrdinalIgnoreCase));
    }

    private static JwtBearerOptions Configure(string scheme, TenantOidcSettings settings)
    {
        var tenants = new TenantSignInSettings(new Dictionary<string, TenantOidcSettings> { ["a"] = settings });
        var options = new JwtBearerOptions();
        new BearerCallerOptions(tenants, allowHttp: false, TimeProvider.System).Configure(scheme, options);
        return options;
    }

    private static async Task<MessageReceivedContext> ReceiveAsync(JwtBearerOptions options, string tenantId)
    {
        var accessor = new TestTenantContextAccessor { Current = new TenantContext(tenantId) };
        var services = new ServiceCollection().AddSingleton<ITenantContextAccessor>(accessor).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme("service-a", displayName: null, typeof(JwtBearerHandler));
        var context = new MessageReceivedContext(http, scheme, options);

        await options.Events.MessageReceived(context);
        return context;
    }

    private sealed class TestTenantContextAccessor : ITenantContextAccessor
    {
        public TenantContext? Current { get; set; }
    }
}
