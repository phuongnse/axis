using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>GET /api/me</c> answers <c>studioAi</c> for a person who may use Studio when the server has <c>Ai:DefaultBinding</c>
/// set, so Studio knows when to show Ask AI, see <c>docs/reference/security.md#roles-and-the-current-user</c>.
/// </summary>
public sealed class StudioAiTests : IAsyncLifetime
{
    private const string HostA = StudioPublishTests.HostA;

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task An_author_on_a_server_with_a_default_binding_gets_studioAi()
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_ai_allowed", CancellationToken);
        await using var plain = StudioPublishTests.NewFactory(folder.Path, [("a", HostA, connectionString)]);
        await using var factory = plain.WithWebHostBuilder(builder => builder.UseSetting("Ai:DefaultBinding", "main"));
        using var employee = factory.CreateClient().SignedInAs("employee");

        using var me = await StudioPublishTests.GetJsonAsync(employee, "/api/me", HostA);

        Assert.True(me.RootElement.GetProperty("studio").GetBoolean());
        Assert.True(me.RootElement.GetProperty("studioAi").GetBoolean());
    }

    [Fact]
    public async Task A_person_without_the_author_role_gets_no_studioAi()
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_ai_no_author", CancellationToken);
        await using var plain = StudioPublishTests.NewFactory(folder.Path, [("a", HostA, connectionString)]);
        await using var factory = plain.WithWebHostBuilder(builder => builder.UseSetting("Ai:DefaultBinding", "main"));
        using var requester = factory.CreateClient().SignedInAs("requester");

        using var me = await StudioPublishTests.GetJsonAsync(requester, "/api/me", HostA);

        Assert.False(me.RootElement.GetProperty("studio").GetBoolean());
        Assert.False(me.RootElement.GetProperty("studioAi").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task An_author_on_a_server_without_a_default_binding_gets_no_studioAi(string? binding)
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await _database.CreateDatabaseAsync($"studio_ai_none_{(binding is null ? "unset" : "blank")}", CancellationToken);
        await using var plain = StudioPublishTests.NewFactory(folder.Path, [("a", HostA, connectionString)]);
        await using var factory = binding is null
            ? plain
            : plain.WithWebHostBuilder(builder => builder.UseSetting("Ai:DefaultBinding", binding));
        using var employee = factory.CreateClient().SignedInAs("employee");

        using var me = await StudioPublishTests.GetJsonAsync(employee, "/api/me", HostA);

        Assert.True(me.RootElement.GetProperty("studio").GetBoolean());
        Assert.False(me.RootElement.GetProperty("studioAi").GetBoolean());
    }
}
