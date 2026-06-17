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

    private static readonly Guid WorkspaceId = Guid.NewGuid();
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
        Invitation.Create(Email.Create(email).Value, WorkspaceId, RoleId, InvitedById);

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedByToken()
    {
        Invitation invitation = MakeInvitation("invite-token@example.com");
        await _sut.AddAsync(invitation);
        await _ctx.SaveChangesAsync();
        Invitation? loaded = await _sut.GetByTokenAsync(invitation.Token);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(invitation.Id);
        loaded.Email.Value.Should().Be("invite-token@example.com");
        loaded.Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public async Task GetByTokenAsync_WhenTokenDoesNotExist_ReturnsNull()
    {
        Invitation? result = await _sut.GetByTokenAsync("nonexistent-token");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPendingByEmailAsync_WhenPendingInvitationExists_ReturnsInvitation()
    {
        Invitation invitation = MakeInvitation("pending@example.com");
        await _sut.AddAsync(invitation);
        await _ctx.SaveChangesAsync();
        Email email = Email.Create("pending@example.com").Value;
        Invitation? loaded = await _sut.GetPendingByEmailAsync(email, WorkspaceId);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(invitation.Id);
    }

    [Fact]
    public async Task GetPendingByEmailAsync_WhenInvitationHasBeenAccepted_ReturnsNull()
    {
        Invitation invitation = MakeInvitation("accepted@example.com");
        await _sut.AddAsync(invitation);
        await _ctx.SaveChangesAsync();

        invitation.Accept();
        await _ctx.SaveChangesAsync();
        Email email = Email.Create("accepted@example.com").Value;
        Invitation? result = await _sut.GetPendingByEmailAsync(email, WorkspaceId);
        result.Should().BeNull();
    }
}
