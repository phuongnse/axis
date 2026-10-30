using System.Net;

namespace Axis.Server.Authentication;

/// <summary>
/// Shortens a client address for an audit record, so a record kept forever no longer points to one
/// device: IPv4 loses its last octet and IPv6 its last 80 bits.
/// </summary>
internal static class ClientAddress
{
    /// <summary>
    /// Returns the address as text with the last IPv4 octet or the last 80 bits of IPv6 set to zero,
    /// or null when there is no address. An IPv4-mapped IPv6 address is shortened as IPv4.
    /// </summary>
    public static string? Shorten(IPAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            bytes[3] = 0;
        }
        else
        {
            Array.Clear(bytes, 6, bytes.Length - 6);
        }

        return new IPAddress(bytes).ToString();
    }
}
