using System.Collections.Concurrent;
using System.Security.Cryptography;
using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

/// <summary>
/// Keeps sessions in memory under random keys, for test hosts whose tenant database is unreachable
/// and that only need some session. The lifetime and the row layout of the real store are tested on
/// PostgreSQL in the integration tests.
/// </summary>
internal sealed class InMemorySessionStore : ITicketStore
{
    private readonly ConcurrentDictionary<string, AuthenticationTicket> _tickets = new();

    public Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _tickets[key] = ticket;
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        _tickets[key] = ticket;
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken) =>
        Task.FromResult(_tickets.GetValueOrDefault(key));

    public Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        _tickets.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket) => throw new NotSupportedException();

    public Task RenewAsync(string key, AuthenticationTicket ticket) => throw new NotSupportedException();

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => throw new NotSupportedException();

    public Task RemoveAsync(string key) => throw new NotSupportedException();
}

internal static class InMemorySessions
{
    /// <summary>Replaces the session store with an <see cref="InMemorySessionStore"/>.</summary>
    public static IWebHostBuilder UseInMemorySessions(this IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
            services.PostConfigure<CookieAuthenticationOptions>(Session.Scheme, options => options.SessionStore = new InMemorySessionStore()));
}
