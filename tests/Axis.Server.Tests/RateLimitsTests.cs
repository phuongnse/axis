using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Claims;
using Axis.Core.Telemetry;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

/// <summary>The partition and the count key that a request gets, and the refusal metric.</summary>
public sealed class RateLimitsTests
{
    [Fact]
    public void A_bearer_caller_on_a_route_that_also_takes_a_session_counts_as_bearer_by_caller_id()
    {
        var context = Request(new RateLimitRoute(Partition.Session, Bearer: true), EndpointCaller.CreatePrincipal("a", "purchasing", "Purchasing", []));

        Assert.Equal((Partition.Bearer, "a|purchasing"), RateLimits.Choose(context, "a"));
    }

    [Fact]
    public void A_session_user_counts_in_the_partition_of_the_route_by_tenant_and_user()
    {
        var user = Session.CreatePrincipal("a", "requester", "Requester", ["employee"]);

        Assert.Equal((Partition.Session, "a|requester"), RateLimits.Choose(Request(new RateLimitRoute(Partition.Session, Bearer: false), user), "a"));
        Assert.Equal((Partition.Publishing, "a|requester"), RateLimits.Choose(Request(new RateLimitRoute(Partition.Publishing, Bearer: false), user), "a"));
        Assert.Equal((Partition.Session, "c|requester"), RateLimits.Choose(Request(new RateLimitRoute(Partition.Session, Bearer: false), user), "c"));
    }

    [Fact]
    public void A_session_does_not_count_as_bearer_on_a_route_that_takes_both()
    {
        var user = Session.CreatePrincipal("a", "requester", "Requester", ["employee"]);

        Assert.Equal((Partition.Session, "a|requester"), RateLimits.Choose(Request(new RateLimitRoute(Partition.Session, Bearer: true), user), "a"));
    }

    [Fact]
    public void Sign_in_counts_by_client_address_only()
    {
        var context = Request(new RateLimitRoute(Partition.SignIn, Bearer: false), new ClaimsPrincipal(), "10.0.0.1");

        Assert.Equal((Partition.SignIn, "10.0.0.1"), RateLimits.Choose(context, "a"));
        Assert.Equal((Partition.SignIn, "10.0.0.1"), RateLimits.Choose(context, "c"));
    }

    [Fact]
    public void The_callback_counts_with_sign_in_whatever_the_route()
    {
        var context = Request(route: null, new ClaimsPrincipal(), "10.0.0.1", path: "/api/auth/CALLBACK");

        Assert.Equal((Partition.SignIn, "10.0.0.1"), RateLimits.Choose(context, "a"));
    }

    [Theory]
    [InlineData("Session", false, "Session")]
    [InlineData("Publishing", false, "Publishing")]
    [InlineData(null, true, "Bearer")]
    [InlineData("Session", true, "Session")]
    public void A_request_with_no_session_or_token_counts_in_the_partition_of_the_route_by_tenant_and_address(
        string? session, bool bearer, string expected)
    {
        var route = new RateLimitRoute(session is null ? null : Enum.Parse<Partition>(session), bearer);
        var context = Request(route, new ClaimsPrincipal(), "10.0.0.2");

        Assert.Equal((Enum.Parse<Partition>(expected), "a|addr:10.0.0.2"), RateLimits.Choose(context, "a"));
    }

    [Fact]
    public void A_request_with_no_client_address_shares_one_key()
    {
        var context = Request(new RateLimitRoute(Partition.SignIn, Bearer: false), new ClaimsPrincipal(), remoteAddress: null);

        Assert.Equal((Partition.SignIn, "unknown"), RateLimits.Choose(context, "a"));
    }

    [Fact]
    public void An_open_route_and_a_request_with_no_route_have_no_limit()
    {
        var user = Session.CreatePrincipal("a", "requester", "Requester", ["employee"]);

        Assert.Null(RateLimits.Choose(Request(new RateLimitRoute(Session: null, Bearer: false, Open: true), user), "a"));
        Assert.Null(RateLimits.Choose(Request(route: null, user), "a"));
    }

    [Fact]
    public void Refusals_count_by_partition()
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == AxisMetrics.MeterName && instrument.Name == AxisMetrics.RateLimitRefusals)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            foreach (var tag in tags)
            {
                keys.Add(tag.Key);
                counts[(string)tag.Value!] = counts.GetValueOrDefault((string)tag.Value!) + measurement;
            }
        });
        listener.Start();
        var refusals = new RateLimitRefusals(services.GetRequiredService<IMeterFactory>());

        refusals.Record(Partition.SignIn);
        refusals.Record(Partition.Session);
        refusals.Record(Partition.Session);
        refusals.Record(Partition.Bearer);
        refusals.Record(Partition.Publishing);

        Assert.Equal(["axis.partition"], keys);
        Assert.Equal(
            new Dictionary<string, long> { ["sign_in"] = 1, ["session"] = 2, ["bearer"] = 1, ["publishing"] = 1 },
            counts);
    }

    private static DefaultHttpContext Request(
        RateLimitRoute? route,
        ClaimsPrincipal principal,
        string? remoteAddress = "10.0.0.9",
        string path = "/api/test")
    {
        var context = new DefaultHttpContext { User = principal };
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = remoteAddress is null ? null : IPAddress.Parse(remoteAddress);
        if (route is not null)
        {
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(route), "test"));
        }

        return context;
    }
}
