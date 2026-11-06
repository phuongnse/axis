using Axis.Server.Mcp;
using Microsoft.AspNetCore.DataProtection;

namespace Axis.Server.Tests;

/// <summary>A session id reads back only on the tenant that made it.</summary>
public sealed class McpSessionTests
{
    private readonly McpSession _sessions = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void A_session_reads_back_on_its_own_tenant()
    {
        var id = _sessions.Create("a", new McpSessionData("person", "client", "2025-11-25"));

        Assert.Equal(new McpSessionData("person", "client", "2025-11-25"), _sessions.TryRead("a", id));
    }

    [Fact]
    public void A_session_of_one_tenant_is_not_a_session_of_another()
    {
        var id = _sessions.Create("a", new McpSessionData("person", "client", "2025-11-25"));

        Assert.Null(_sessions.TryRead("b", id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-session")]
    public void Text_that_is_not_a_session_reads_as_none(string text) => Assert.Null(_sessions.TryRead("a", text));
}
