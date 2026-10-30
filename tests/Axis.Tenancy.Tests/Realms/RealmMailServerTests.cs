using Axis.Tenancy.Realms;

namespace Axis.Tenancy.Tests.Realms;

public sealed class RealmMailServerTests
{
    [Fact]
    public void Tls_on_port_465_is_ssl()
    {
        var server = RealmMailServer.Of(new EmailSettings("smtp.example.test", 465, true, null, null, "noreply@example.test"), false);

        Assert.Equal("smtp.example.test", server["host"]);
        Assert.Equal("465", server["port"]);
        Assert.Equal("noreply@example.test", server["from"]);
        Assert.Equal("true", server["ssl"]);
        Assert.Equal("false", server["starttls"]);
        Assert.Equal("false", server["auth"]);
        Assert.DoesNotContain("user", server.Keys);
        Assert.DoesNotContain("password", server.Keys);
    }

    [Fact]
    public void Tls_on_another_port_is_starttls()
    {
        var server = RealmMailServer.Of(new EmailSettings("smtp.example.test", 587, true, null, null, "noreply@example.test"), false);

        Assert.Equal("587", server["port"]);
        Assert.Equal("false", server["ssl"]);
        Assert.Equal("true", server["starttls"]);
    }

    [Fact]
    public void Tls_off_is_plain_only_where_plain_smtp_is_allowed()
    {
        var settings = new EmailSettings("mailpit", 1025, false, null, null, "axis@localhost");

        var development = RealmMailServer.Of(settings, true);
        Assert.Equal("mailpit", development["host"]);
        Assert.Equal("false", development["ssl"]);
        Assert.Equal("false", development["starttls"]);

        Assert.Empty(RealmMailServer.Of(settings, false));
    }

    [Fact]
    public void No_settings_or_no_host_gives_an_empty_server()
    {
        Assert.Empty(RealmMailServer.Of(null, true));
        Assert.Empty(RealmMailServer.Of(new EmailSettings(null, 587, true, null, null, null), true));
    }

    [Fact]
    public void A_user_with_a_password_turns_authentication_on()
    {
        var server = RealmMailServer.Of(
            new EmailSettings("smtp.example.test", 587, true, "axis", new SecretValue("p4ss-w0rd-x9"), "noreply@example.test"), false);

        Assert.Equal("true", server["auth"]);
        Assert.Equal("axis", server["user"]);
        Assert.Equal("p4ss-w0rd-x9", server["password"]);
    }
}
