using Axis.Tenancy.Realms;

namespace Axis.Tenancy.Tests.Realms;

public sealed class SiteClientSecretTests
{
    [Fact]
    public void The_same_inputs_give_the_same_value()
    {
        Assert.Equal(SiteClientSecret.Derive("platform-secret", "site-purchasing"), SiteClientSecret.Derive("platform-secret", "site-purchasing"));
    }

    [Fact]
    public void Another_client_id_gives_another_value()
    {
        Assert.NotEqual(SiteClientSecret.Derive("platform-secret", "site-purchasing"), SiteClientSecret.Derive("platform-secret", "site-leave"));
    }

    [Fact]
    public void Another_platform_secret_gives_another_value()
    {
        Assert.NotEqual(SiteClientSecret.Derive("platform-secret", "site-purchasing"), SiteClientSecret.Derive("other-secret", "site-purchasing"));
    }

    [Fact]
    public void The_value_is_base64url_without_padding()
    {
        var value = SiteClientSecret.Derive("platform-secret", "site-purchasing");

        Assert.Equal(43, value.Length);
        Assert.DoesNotContain('+', value);
        Assert.DoesNotContain('/', value);
        Assert.DoesNotContain('=', value);
    }

    [Theory]
    [InlineData(null, "site-purchasing")]
    [InlineData("", "site-purchasing")]
    [InlineData("platform-secret", null)]
    [InlineData("platform-secret", "")]
    public void A_missing_argument_throws(string? platformSecret, string? clientId)
    {
        Assert.ThrowsAny<ArgumentException>(() => SiteClientSecret.Derive(platformSecret!, clientId!));
    }
}
