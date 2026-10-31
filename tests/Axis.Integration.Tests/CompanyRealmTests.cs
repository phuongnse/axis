using System.Net;
using System.Text.Json;

namespace Axis.Integration.Tests;

/// <summary>
/// The organization realm <c>company</c> in samples/keycloak plays an organization's own identity provider. Its person
/// signs in with a password and no code, and the confidential client <c>axis-broker</c> gives the ID token a
/// <c>groups</c> claim of plain group names.
/// </summary>
public sealed class CompanyRealmTests(KeycloakFixture keycloak)
{
    [Fact]
    public async Task Issues_the_person_an_id_token_with_plain_group_names_and_no_code()
    {
        using var response = await RequestTokenAsync("pat", KeycloakFixture.Password);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);

        using var tokens = JsonDocument.Parse(body);
        using var payload = DevelopmentRealmTests.DecodePayload(tokens.RootElement.GetProperty("id_token").GetString()!);
        var claims = payload.RootElement;
        Assert.Equal("pat", claims.GetProperty("sub").GetString());
        Assert.Equal("pat@company.example", claims.GetProperty("email").GetString());
        Assert.Equal(["employees"], claims.GetProperty("groups").EnumerateArray().Select(group => group.GetString()));
    }

    [Fact]
    public async Task Refuses_a_wrong_password()
    {
        using var response = await RequestTokenAsync("pat", "wrong");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        using var error = JsonDocument.Parse(body);
        Assert.Equal("invalid_grant", error.RootElement.GetProperty("error").GetString());
    }

    private async Task<HttpResponseMessage> RequestTokenAsync(string username, string password)
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        using var client = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = KeycloakFixture.CompanyBrokerClientId,
            ["client_secret"] = KeycloakFixture.CompanyBrokerClientSecret,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid",
        });
        return await client.PostAsync(new Uri(baseAddress, "realms/company/protocol/openid-connect/token"), form, TestContext.Current.CancellationToken);
    }
}
