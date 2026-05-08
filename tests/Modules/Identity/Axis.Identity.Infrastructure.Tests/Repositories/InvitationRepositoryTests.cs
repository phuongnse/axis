using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

[Collection("IdentityDb")]
public class InvitationRepositoryTests(IdentityDatabaseFixture db) : IAsyncLifetime
{
    private IdentityDbContext _ctx = null!;
    private InvitationRepository _sut = null!;

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid InvitedById = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new InvitationRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static Invitation MakeInvitation(string email) =>
        Invitation.Create(Email.Create(email).Value, OrgId, RoleId, InvitedById);

    [Fact]
    public async Task AddAsync_and_GetByTokenAsync_round_trip()
    {
        var invitation = MakeInvitation("invite-token@example.com");
        await _sut.AddAsync(invitation);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByTokenAsync(invitation.Token);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(invitation.Id);
        loaded.Email.Value.Should().Be("invite-token@example.com");
        loaded.Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public async Task GetByTokenAsync_returns_null_for_unknown_token()
    {
        var result = await _sut.GetByTokenAsync("nonexistent-token");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPendingByEmailAsync_returns_pending_invitation()
    {
        var invitation = MakeInvitation("pending@example.com");
        await _sut.AddAsync(invitation);
        await _ctx.SaveChangesAsync();

        var email = Email.Create("pending@example.com").Value;
        var loaded = await _sut.GetPendingByEmailAsync(email, OrgId);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(invitation.Id);
    }

    [Fact]
    public async Task GetPendingByEmailAsync_returns_null_after_accepted()
    {
        var invitation = MakeInvitation("accepted@example.com");
        await _sut.AddAsync(invitation);
        await _ctx.SaveChangesAsync();

        invitation.Accept();
        await _ctx.SaveChangesAsync();

        var email = Email.Create("accepted@example.com").Value;
        var result = await _sut.GetPendingByEmailAsync(email, OrgId);
        result.Should().BeNull();
    }
}
