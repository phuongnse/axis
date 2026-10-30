using System.Net;
using Axis.Server.Authentication;

namespace Axis.Server.Tests;

public sealed class ClientAddressTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.0")]
    [InlineData("2001:db8:1:2:3:4:5:6", "2001:db8:1::")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.0")]
    [InlineData("::ffff:cb00:7107", "203.0.113.0")]
    public void An_address_loses_its_last_part(string address, string expected)
    {
        Assert.Equal(expected, ClientAddress.Shorten(IPAddress.Parse(address)));
    }

    [Fact]
    public void A_missing_address_stays_null()
    {
        Assert.Null(ClientAddress.Shorten(null));
    }
}
