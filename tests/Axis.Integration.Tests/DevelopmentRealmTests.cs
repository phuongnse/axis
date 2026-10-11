using System.Buffers.Text;
using System.Net;
using System.Text.Json;

namespace Axis.Integration.Tests;

/// <summary>The development realm in samples/keycloak issues tokens for the purchase request users.</summary>
public sealed class DevelopmentRealmTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private Uri? _authority;

    public static TheoryData<string, string, string[]> Users => new()
    {
        { "anna", "Anna Employee", ["employee"] },
        { "lan", "Lan Employee", ["employee"] },
        { "binh", "Binh Engineering Manager", ["department-manager"] },
        { "chau", "Chau Operations Manager", ["department-manager"] },
        { "dung", "Dung Finance Reviewer", ["finance-reviewer"] },
    };

    public async ValueTask InitializeAsync() => _authority = await keycloak.StartAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(Users))]
    public async Task Issues_tokens_with_the_user_id_name_and_roles(string id, string displayName, string[] roles)
    {
        using var response = await RequestTokenAsync(id, KeycloakFixture.Password);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);

        using var tokens = JsonDocument.Parse(body);
        foreach (var name in new[] { "id_token", "access_token" })
        {
            using var payload = DecodePayload(tokens.RootElement.GetProperty(name).GetString()!);
            var claims = payload.RootElement;
            Assert.Equal(id, claims.GetProperty("sub").GetString());
            Assert.Equal(displayName, claims.GetProperty("name").GetString());
            Assert.Equal(JsonValueKind.Array, claims.GetProperty("roles").ValueKind);
            Assert.Equal(roles, claims.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        }
    }

    [Fact]
    public async Task Refuses_a_wrong_password()
    {
        using var response = await RequestTokenAsync("anna", "wrong");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var error = JsonDocument.Parse(body);
        Assert.Equal("invalid_grant", error.RootElement.GetProperty("error").GetString());
    }

    // The password grant gets a token without a login page.
    private async Task<HttpResponseMessage> RequestTokenAsync(string username, string password)
    {
        using var client = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = KeycloakFixture.ClientId,
            ["client_secret"] = KeycloakFixture.ClientSecret,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid",
        });
        return await client.PostAsync(new Uri($"{_authority}/protocol/openid-connect/token"), form, TestContext.Current.CancellationToken);
    }

    private static JsonDocument DecodePayload(string token) =>
        JsonDocument.Parse(Base64Url.DecodeFromChars(token.Split('.')[1]));
}
