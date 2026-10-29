namespace Axis.Tenancy.Tests;

public sealed class TenantRealmAddressTests
{
    [Theory]
    [InlineData("https://idp.example.test/realms/acme", "https://idp.example.test/", "acme")]
    [InlineData("https://idp.example.test/realms/acme/", "https://idp.example.test/", "acme")]
    [InlineData("http://localhost:8080/auth/realms/acme", "http://localhost:8080/auth/", "acme")]
    [InlineData("https://idp.example.test/realms/my%20realm?x=1#y", "https://idp.example.test/", "my realm")]
    public void Split_gives_the_base_address_and_the_realm_of_an_authority(string authority, string baseAddress, string realm)
    {
        var split = TenantRealmAddress.Split(authority);

        Assert.NotNull(split);
        Assert.Equal(new Uri(baseAddress), split.Value.BaseAddress);
        Assert.Equal(realm, split.Value.Realm);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("ftp://idp.example.test/realms/acme")]
    [InlineData("https://idp.example.test/realms/")]
    [InlineData("https://idp.example.test/realm/acme")]
    [InlineData("https://idp.example.test/acme")]
    [InlineData("https://idp.example.test/realms/acme//")]
    public void Split_gives_nothing_for_an_authority_that_does_not_end_in_a_realm(string? authority)
    {
        Assert.Null(TenantRealmAddress.Split(authority));
    }
}
