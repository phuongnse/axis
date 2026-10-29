using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class CurrentUserUsersTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("rita", true)]
    [InlineData("requester", false)]
    public async Task Users_is_true_only_for_a_person_who_holds_the_administrator_role(string user, bool expected)
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Users:AdministratorRole", "reader"));
        using var client = factory.CreateClient().SignedInAs(user);

        Assert.Equal(expected, await ReadUsersAsync(client));
    }

    [Theory]
    [InlineData("rita")]
    [InlineData("requester")]
    public async Task Users_is_false_for_everyone_when_the_administrator_role_is_not_set(string user)
    {
        using var client = fixture.CreateClient().SignedInAs(user);

        Assert.False(await ReadUsersAsync(client));
    }

    private static async Task<bool> ReadUsersAsync(HttpClient client)
    {
        using var request = Request("/api/me", HostA);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var me = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return me.RootElement.GetProperty("users").GetBoolean();
    }
}
