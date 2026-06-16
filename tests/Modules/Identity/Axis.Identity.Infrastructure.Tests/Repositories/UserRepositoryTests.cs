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

    private static readonly Guid TenantId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new UserRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static User MakeUser(string email) =>
        User.Create("Jane", "Doe", Email.Create(email).Value);

    private static TenantMembership MakeMembership(User user, Guid tenantId, params Guid[] roleIds)
    {
        TenantMembership membership = TenantMembership.Create(user.Id, tenantId);
        foreach (Guid roleId in roleIds)
            membership.AssignRole(roleId);
        return membership;
    }

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        User user = MakeUser("getbyid@example.com");
        await _sut.AddAsync(user);
        await _ctx.TenantMemberships.AddAsync(MakeMembership(user, TenantId));
        await _ctx.SaveChangesAsync();
        User? loaded = await _sut.GetByIdAsync(user.Id, TenantId);

        loaded.Should().NotBeNull();
        loaded!.Email.Value.Should().Be("getbyid@example.com");
        loaded.FirstName.Should().Be("Jane");
        loaded.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public async Task GetByEmailAsync_WhenEmailExistsInTenant_ReturnsUser()
    {
        User user = MakeUser("byemail@example.com");
        await _sut.AddAsync(user);
        await _ctx.TenantMemberships.AddAsync(MakeMembership(user, TenantId));
        await _ctx.SaveChangesAsync();
        Email email = Email.Create("byemail@example.com").Value;
        User? loaded = await _sut.GetByEmailAsync(email, TenantId);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetByEmailAsync_WhenEmailBelongsToDifferentTenant_ReturnsNull()
    {
        User user = MakeUser("diffTenant@example.com");
        await _sut.AddAsync(user);
        await _ctx.TenantMemberships.AddAsync(MakeMembership(user, TenantId));
        await _ctx.SaveChangesAsync();
        Email email = Email.Create("diffTenant@example.com").Value;
        User? result = await _sut.GetByEmailAsync(email, Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task EmailExistsPlatformWideAsync_WhenEmailExistsInAnyTenant_ReturnsTrue()
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
        TenantMembership membership = MakeMembership(user, TenantId, roleId);
        await _sut.AddAsync(user);
        await _ctx.TenantMemberships.AddAsync(membership);
        await _ctx.SaveChangesAsync();
        TenantMembership? loaded = await _ctx.TenantMemberships.FindAsync(membership.Id);

        loaded!.RoleIds.Should().ContainSingle().Which.Should().Be(roleId);
    }

    [Fact]
    public async Task AddAsync_WhenUserHasPasswordHash_PersistsAndReloadsPasswordHash()
    {
        User user = MakeUser("withhash@example.com");
        user.SetPasswordHash("$2a$12$fakehashvalue");
        await _sut.AddAsync(user);
        await _ctx.TenantMemberships.AddAsync(MakeMembership(user, TenantId));
        await _ctx.SaveChangesAsync();
        User? loaded = await _sut.GetByIdAsync(user.Id, TenantId);

        loaded!.PasswordHash.Should().Be("$2a$12$fakehashvalue");
    }

    [Fact]
    public async Task CountAdminsAsync_WhenMultipleUsersExist_CountsOnlyUsersWithAdminRole()
    {
        Guid adminRoleId = Guid.NewGuid();
        Guid adminTenantId = Guid.NewGuid();

        User admin1 = User.Create("A", "One", Email.Create($"admin1-{adminTenantId:N}@example.com").Value);
        TenantMembership admin1Membership = MakeMembership(admin1, adminTenantId, adminRoleId);
        User admin2 = User.Create("A", "Two", Email.Create($"admin2-{adminTenantId:N}@example.com").Value);
        TenantMembership admin2Membership = MakeMembership(admin2, adminTenantId, adminRoleId);
        User nonAdmin = User.Create("B", "One", Email.Create($"nonadmin-{adminTenantId:N}@example.com").Value);
        TenantMembership nonAdminMembership = MakeMembership(nonAdmin, adminTenantId);

        await _sut.AddAsync(admin1);
        await _sut.AddAsync(admin2);
        await _sut.AddAsync(nonAdmin);
        await _ctx.TenantMemberships.AddRangeAsync(admin1Membership, admin2Membership, nonAdminMembership);
        await _ctx.SaveChangesAsync();

        int count = await _sut.CountAdminsAsync(adminTenantId, adminRoleId);
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetByIdAsync_WhenUserBelongsToDifferentTenant_ReturnsNull()
    {
        User user = MakeUser($"crossTenant-{Guid.NewGuid():N}@example.com");
        await _sut.AddAsync(user);
        await _ctx.TenantMemberships.AddAsync(MakeMembership(user, TenantId));
        await _ctx.SaveChangesAsync();

        User? result = await _sut.GetByIdAsync(user.Id, Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindByEmailGloballyAsync_WhenEmailExistsInAnyTenant_ReturnsUser()
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
