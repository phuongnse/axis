namespace Axis.ControlPlane.Tests;

public sealed class ControlPlaneOptionsTests
{
    [Theory]
    [InlineData("ControlPlane:Region")]
    [InlineData("ControlPlane:TenantDomain")]
    [InlineData("ControlPlane:ConnectionString")]
    [InlineData("ControlPlane:MailFrom")]
    [InlineData("ControlPlane:Mail:Host")]
    public void A_missing_setting_stops_the_start_and_the_message_names_it(string setting)
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?> { [setting] = string.Empty });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_region_with_capital_letters_stops_the_start()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?> { ["ControlPlane:Region"] = "VN" });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Region", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tenant_domain_that_is_not_a_domain_name_stops_the_start()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?> { ["ControlPlane:TenantDomain"] = "not a domain" });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:TenantDomain", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_missing_setting_is_named_at_once()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?>
        {
            ["ControlPlane:Region"] = string.Empty,
            ["ControlPlane:TenantDomain"] = string.Empty,
            ["ControlPlane:ConnectionString"] = string.Empty,
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Region", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ControlPlane:TenantDomain", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ControlPlane:ConnectionString", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_Development_a_connection_without_VerifyFull_stops_the_start_and_the_message_holds_no_password()
    {
        using var factory = ControlPlaneFactory.Create(environment: "Production");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:ConnectionString", exception.Message, StringComparison.Ordinal);
        Assert.Contains("VerifyFull", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-password", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_connection_string_that_does_not_parse_stops_the_start_and_the_message_holds_no_value()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?>
        {
            ["ControlPlane:ConnectionString"] = "Password=secret-password;NoSuchKey=1",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:ConnectionString", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-password", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mail_sender_that_is_not_an_address_stops_the_start()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?> { ["ControlPlane:MailFrom"] = "not an address" });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:MailFrom", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mail_port_out_of_range_stops_the_start()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?> { ["ControlPlane:Mail:Port"] = "70000" });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Mail:Port", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_Development_a_mail_relay_without_TLS_stops_the_start()
    {
        using var factory = ControlPlaneFactory.Create(
            environment: "Production",
            settings: new Dictionary<string, string?>
            {
                ["ControlPlane:ConnectionString"] = ControlPlaneFactory.UnreachableConnectionString + ";SSL Mode=VerifyFull",
                ["ControlPlane:Mail:UseTls"] = "false",
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Mail:UseTls", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mail_user_name_without_a_password_stops_the_start_and_the_message_holds_no_value()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?> { ["ControlPlane:Mail:UserName"] = "relay-user" });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Mail:Password", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("relay-user", exception.Message, StringComparison.Ordinal);
    }
}
