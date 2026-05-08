using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Services;

[Collection("IdentityDb")]
public class SessionStoreServiceTests(IdentityDatabaseFixture db) : IAsyncLifetime
{
    private IdentityDbContext _ctx = null!;
    private SessionStoreService _sut = null!;
    private RefreshTokenStore _tokenStore = null!;

    private static readonly Guid OrgId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _tokenStore = new RefreshTokenStore(_ctx);
        _sut = new SessionStoreService(_tokenStore);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task GetByUserAsync_marks_current_session_correctly()
    {
        var userId = Guid.NewGuid();
        var id1 = await _tokenStore.CreateAsync(userId, OrgId, "sess_hash1", "Chrome", DateTime.UtcNow.AddDays(7));
        var id2 = await _tokenStore.CreateAsync(userId, OrgId, "sess_hash2", "Firefox", DateTime.UtcNow.AddDays(7));

        var sessions = await _sut.GetByUserAsync(userId, id1.ToString());

        sessions.Should().HaveCount(2);
        sessions.Single(s => s.SessionId == id1.ToString()).IsCurrentSession.Should().BeTrue();
        sessions.Single(s => s.SessionId == id2.ToString()).IsCurrentSession.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_invalidates_the_session()
    {
        var userId = Guid.NewGuid();
        var id = await _tokenStore.CreateAsync(userId, OrgId, "sess_revoke", "UA", DateTime.UtcNow.AddDays(7));

        await _sut.RevokeAsync(id.ToString(), userId);

        var sessions = await _sut.GetByUserAsync(userId, "other");
        sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task RevokeAllAsync_removes_all_sessions()
    {
        var userId = Guid.NewGuid();
        await _tokenStore.CreateAsync(userId, OrgId, "all1", "UA", DateTime.UtcNow.AddDays(7));
        await _tokenStore.CreateAsync(userId, OrgId, "all2", "UA", DateTime.UtcNow.AddDays(7));

        await _sut.RevokeAllAsync(userId);

        var sessions = await _sut.GetByUserAsync(userId, "none");
        sessions.Should().BeEmpty();
    }
}
