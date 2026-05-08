using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Services;

[Collection("IdentityDb")]
public class RefreshTokenStoreTests(IdentityDatabaseFixture db) : IAsyncLifetime
{
    private IdentityDbContext _ctx = null!;
    private RefreshTokenStore _sut = null!;

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new RefreshTokenStore(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task CreateAsync_persists_token_and_returns_id()
    {
        var id = await _sut.CreateAsync(UserId, OrgId, "hash_create", "Mozilla/5.0", DateTime.UtcNow.AddDays(7));

        id.Should().NotBeEmpty();
        var found = await _sut.FindByHashAsync("hash_create");
        found.Should().NotBeNull();
        found!.UserId.Should().Be(UserId);
        found.OrganizationId.Should().Be(OrgId);
    }

    [Fact]
    public async Task FindByHashAsync_returns_null_for_revoked_token()
    {
        var id = await _sut.CreateAsync(UserId, OrgId, "hash_revoked", "UA", DateTime.UtcNow.AddDays(7));
        await _sut.RevokeAsync(id);

        var found = await _sut.FindByHashAsync("hash_revoked");

        found.Should().BeNull();
    }

    [Fact]
    public async Task FindByHashAsync_returns_null_for_expired_token()
    {
        await _sut.CreateAsync(UserId, OrgId, "hash_expired", "UA", DateTime.UtcNow.AddSeconds(-1));

        var found = await _sut.FindByHashAsync("hash_expired");

        found.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveByUserAsync_excludes_revoked_and_expired()
    {
        var activeUserId = Guid.NewGuid();
        await _sut.CreateAsync(activeUserId, OrgId, "active1", "UA1", DateTime.UtcNow.AddDays(7));
        var revokedId = await _sut.CreateAsync(activeUserId, OrgId, "active2", "UA2", DateTime.UtcNow.AddDays(7));
        await _sut.CreateAsync(activeUserId, OrgId, "active3", "UA3", DateTime.UtcNow.AddSeconds(-1));
        await _sut.RevokeAsync(revokedId);

        var active = await _sut.GetActiveByUserAsync(activeUserId);

        active.Should().HaveCount(1);
        active[0].DeviceInfo.Should().Be("UA1");
    }

    [Fact]
    public async Task RevokeAllForUserAsync_revokes_all_active_tokens()
    {
        var targetUserId = Guid.NewGuid();
        await _sut.CreateAsync(targetUserId, OrgId, "bulk1", "UA", DateTime.UtcNow.AddDays(7));
        await _sut.CreateAsync(targetUserId, OrgId, "bulk2", "UA", DateTime.UtcNow.AddDays(7));
        await _sut.CreateAsync(targetUserId, OrgId, "bulk3", "UA", DateTime.UtcNow.AddDays(7));

        await _sut.RevokeAllForUserAsync(targetUserId);

        var remaining = await _sut.GetActiveByUserAsync(targetUserId);
        remaining.Should().BeEmpty();
    }
}
