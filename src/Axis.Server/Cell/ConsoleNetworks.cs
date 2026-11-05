using System.Net;
using Axis.Tenancy;

namespace Axis.Server.CellApi;

/// <summary>The network limit of the cell console host, see <see cref="CellOptions.ConsoleAllowedNetworks"/>.</summary>
internal static class ConsoleNetworks
{
    /// <summary>
    /// Refuses a request to <see cref="CellOptions.ConsoleHost"/> with a <c>403</c> when its client address is in none of the
    /// ranges of <see cref="CellOptions.ConsoleAllowedNetworks"/>, or when it has no client address. It runs after the forwarded
    /// headers, so the address is the real one, and before tenant resolution and sign-in, so a refused request does no work.
    /// Other hosts, and every host when the list is empty, are not checked.
    /// </summary>
    public static IApplicationBuilder UseConsoleNetworks(this IApplicationBuilder app, CellOptions cell)
    {
        if (!cell.IsCell || string.IsNullOrWhiteSpace(cell.ConsoleHost) || cell.ConsoleAllowedNetworks.Count == 0)
        {
            return app;
        }

        var networks = cell.ConsoleAllowedNetworks.Select(IPNetwork.Parse).ToArray();
        return app.Use(async (context, next) =>
        {
            if (CellOperatorSession.IsConsoleHost(context, cell.ConsoleHost) && !IsAllowed(context.Connection.RemoteIpAddress, networks))
            {
                await Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "This address may not reach the cell console.")
                    .ExecuteAsync(context);
                return;
            }

            await next(context);
        });
    }

    // An IPv4 client on a dual-stack socket arrives as ::ffff:a.b.c.d, which no IPv4 range contains.
    private static bool IsAllowed(IPAddress? address, IPNetwork[] networks)
    {
        if (address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return networks.Any(network => network.Contains(address));
    }
}
