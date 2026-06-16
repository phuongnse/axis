using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

[Collection("IdentityDb")]
public class UserRepositoryTests(IdentityDatabaseFixture db) : IAsyncLifetime
{
    private IdentityDbContext _ctx = null!;
    private UserRepository _sut = null!;

    private static readonly Guid TeamAccountId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new UserRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static User MakeUser(string email) =>
        User.Create("Jane", "Doe", Email.Create(email).Value);

    private static TeamAccountMembership MakeMembership(User user, Guid teamAccountId, params Guid[] roleIds)
    {
        TeamAccountMembership membership = TeamAccountMembership.Create(user.Id, teamAccountId);
        foreach (Guid roleId in roleIds)
            membership.AssignRole(roleId);
        return membership;
    }

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        User user = MakeUser("getbyid@example.com");
        await _sut.AddAsync(user);
        await _ctx.TeamAccountMemberships.AddAsync(MakeMembership(user, TeamAccountId));
        await _ctx.SaveChangesAsync();
        User? loaded = await _sut.GetByIdAsync(user.Id, TeamAccountId);

        loaded.Should().NotBeNull();
        loaded!.Email.Value.Should().Be("getbyid@example.com");
        loaded.FirstName.Should().Be("Jane");
        loaded.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public async Task GetByEmailAsync_WhenEmailExistsInTeamAccount_ReturnsUser()
    {
        User user = MakeUser("byemail@example.com");
        await _sut.AddAsync(user);
        await _ctx.TeamAccountMemberships.AddAsync(MakeMembership(user, TeamAccountId));
        await _ctx.SaveChangesAsync();
        Email email = Email.Create("byemail@example.com").Value;
        User? loaded = await _sut.GetByEmailAsync(email, TeamAccountId);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetByEmailAsync_WhenEmailBelongsToDifferentTeamAccount_ReturnsNull()
    {
        User user = MakeUser("diffteam@example.com");
        await _sut.AddAsync(user);
        await _ctx.TeamAccountMemberships.AddAsync(MakeMembership(user, TeamAccountId));
        await _ctx.SaveChangesAsync();
        Email email = Email.Create("diffteam@example.com").Value;
        User? result = await _sut.GetByEmailAsync(email, Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task EmailExistsPlatformWideAsync_WhenEmailExistsInAnyTeamAccount_ReturnsTrue()
    {
        User user = MakeUser("platform@example.com");
        await _sut.AddAsync(user);
        await _ctx.SaveChangesAsync();
        Email email = Email.Create("platform@example.com").Value;
        bool exists = await _sut.EmailExistsPlatformWideAsync(email);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task EmailExistsPlatformWideAsync_WhenEmailDoesNotExist_ReturnsFalse()
    {
        Email email = Email.Create("nobody@example.com").Value;
        bool exists = await _sut.EmailExistsPlatformWideAsync(email);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_WhenMembershipHasRoles_PersistsAndReloadsRoleIds()
    {
        Guid roleId = Guid.NewGuid();
        User user = MakeUser("withrole@example.com");
        TeamAccountMembership membership = MakeMembership(user, TeamAccountId, roleId);
        await _sut.AddAsync(user);
        await _ctx.TeamAccountMemberships.AddAsync(membership);
        await _ctx.SaveChangesAsync();
        TeamAccountMembership? loaded = await _ctx.TeamAccountMemberships.FindAsync(membership.Id);

        loaded!.RoleIds.Should().ContainSingle().Which.Should().Be(roleId);
    }

    [Fact]
    public async Task AddAsync_WhenUserHasPasswordHash_PersistsAndReloadsPasswordHash()
    {
        User user = MakeUser("withhash@example.com");
        user.SetPasswordHash("$2a$12$fakehashvalue");
        await _sut.AddAsync(user);
        await _ctx.TeamAccountMemberships.AddAsync(MakeMembership(user, TeamAccountId));
        await _ctx.SaveChangesAsync();
        User? loaded = await _sut.GetByIdAsync(user.Id, TeamAccountId);

        loaded!.PasswordHash.Should().Be("$2a$12$fakehashvalue");
    }

    [Fact]
    public async Task CountAdminsAsync_WhenMultipleUsersExist_CountsOnlyUsersWithAdminRole()
    {
        Guid adminRoleId = Guid.NewGuid();
        Guid adminTeamAccountId = Guid.NewGuid();

        User admin1 = User.Create("A", "One", Email.Create($"admin1-{adminTeamAccountId:N}@example.com").Value);
        TeamAccountMembership admin1Membership = MakeMembership(admin1, adminTeamAccountId, adminRoleId);
        User admin2 = User.Create("A", "Two", Email.Create($"admin2-{adminTeamAccountId:N}@example.com").Value);
        TeamAccountMembership admin2Membership = MakeMembership(admin2, adminTeamAccountId, adminRoleId);
        User nonAdmin = User.Create("B", "One", Email.Create($"nonadmin-{adminTeamAccountId:N}@example.com").Value);
        TeamAccountMembership nonAdminMembership = MakeMembership(nonAdmin, adminTeamAccountId);

        await _sut.AddAsync(admin1);
        await _sut.AddAsync(admin2);
        await _sut.AddAsync(nonAdmin);
        await _ctx.TeamAccountMemberships.AddRangeAsync(admin1Membership, admin2Membership, nonAdminMembership);
        await _ctx.SaveChangesAsync();

        int count = await _sut.CountAdminsAsync(adminTeamAccountId, adminRoleId);
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetByIdAsync_WhenUserBelongsToDifferentTeamAccount_ReturnsNull()
    {
        User user = MakeUser($"crossteam-account-{Guid.NewGuid():N}@example.com");
        await _sut.AddAsync(user);
        await _ctx.TeamAccountMemberships.AddAsync(MakeMembership(user, TeamAccountId));
        await _ctx.SaveChangesAsync();

        User? result = await _sut.GetByIdAsync(user.Id, Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindByEmailGloballyAsync_WhenEmailExistsInAnyTeamAccount_ReturnsUser()
    {
        User user = MakeUser($"global-{Guid.NewGuid():N}@example.com");
        await _sut.AddAsync(user);
        await _ctx.SaveChangesAsync();

        User? result = await _sut.FindByEmailGloballyAsync(user.Email);

        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task FindByEmailGloballyAsync_WhenEmailDoesNotExist_ReturnsNull()
    {
        Email email = Email.Create($"notfound-{Guid.NewGuid():N}@example.com").Value;
        User? result = await _sut.FindByEmailGloballyAsync(email);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdPlatformWideAsync_WhenUserExists_ReturnsUser()
    {
        User user = MakeUser($"platformwide-{Guid.NewGuid():N}@example.com");
        await _sut.AddAsync(user);
        await _ctx.SaveChangesAsync();

        User? result = await _sut.GetByIdPlatformWideAsync(user.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetByIdPlatformWideAsync_WhenUserDoesNotExist_ReturnsNull()
    {
        User? result = await _sut.GetByIdPlatformWideAsync(Guid.NewGuid());
        result.Should().BeNull();
    }
}
