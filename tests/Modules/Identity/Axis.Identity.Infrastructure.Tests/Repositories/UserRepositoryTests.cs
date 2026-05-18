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

    private static readonly Guid OrgId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new UserRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static User MakeUser(string email) =>
        User.Create("Jane", "Doe", Email.Create(email).Value, OrgId);

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        var user = MakeUser("getbyid@example.com");
        await _sut.AddAsync(user);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(user.Id, OrgId);

        loaded.Should().NotBeNull();
        loaded!.Email.Value.Should().Be("getbyid@example.com");
        loaded.FirstName.Should().Be("Jane");
        loaded.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public async Task GetByEmailAsync_WhenEmailExistsInOrg_ReturnsUser()
    {
        var user = MakeUser("byemail@example.com");
        await _sut.AddAsync(user);
        await _ctx.SaveChangesAsync();

        var email = Email.Create("byemail@example.com").Value;
        var loaded = await _sut.GetByEmailAsync(email, OrgId);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetByEmailAsync_WhenEmailBelongsToDifferentOrg_ReturnsNull()
    {
        var user = MakeUser("difforg@example.com");
        await _sut.AddAsync(user);
        await _ctx.SaveChangesAsync();

        var email = Email.Create("difforg@example.com").Value;
        var result = await _sut.GetByEmailAsync(email, Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task EmailExistsPlatformWideAsync_WhenEmailExistsInAnyOrg_ReturnsTrue()
    {
        var user = MakeUser("platform@example.com");
        await _sut.AddAsync(user);
        await _ctx.SaveChangesAsync();

        var email = Email.Create("platform@example.com").Value;
        var exists = await _sut.EmailExistsPlatformWideAsync(email);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task EmailExistsPlatformWideAsync_WhenEmailDoesNotExist_ReturnsFalse()
    {
        var email = Email.Create("nobody@example.com").Value;
        var exists = await _sut.EmailExistsPlatformWideAsync(email);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_WhenUserHasRoles_PersistsAndReloadsRoleIds()
    {
        var roleId = Guid.NewGuid();
        var user = MakeUser("withrole@example.com");
        user.AssignRole(roleId);
        await _sut.AddAsync(user);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(user.Id, OrgId);

        loaded!.RoleIds.Should().ContainSingle().Which.Should().Be(roleId);
    }

    [Fact]
    public async Task AddAsync_WhenUserHasPasswordHash_PersistsAndReloadsPasswordHash()
    {
        var user = MakeUser("withhash@example.com");
        user.SetPasswordHash("$2a$12$fakehashvalue");
        await _sut.AddAsync(user);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(user.Id, OrgId);

        loaded!.PasswordHash.Should().Be("$2a$12$fakehashvalue");
    }

    [Fact]
    public async Task CountAdminsAsync_WhenMultipleUsersExist_CountsOnlyUsersWithAdminRole()
    {
        var adminRoleId = Guid.NewGuid();
        var adminOrgId = Guid.NewGuid();

        var admin1 = User.Create("A", "One", Email.Create($"admin1-{adminOrgId:N}@example.com").Value, adminOrgId);
        admin1.AssignRole(adminRoleId);
        var admin2 = User.Create("A", "Two", Email.Create($"admin2-{adminOrgId:N}@example.com").Value, adminOrgId);
        admin2.AssignRole(adminRoleId);
        var nonAdmin = User.Create("B", "One", Email.Create($"nonadmin-{adminOrgId:N}@example.com").Value, adminOrgId);

        await _sut.AddAsync(admin1);
        await _sut.AddAsync(admin2);
        await _sut.AddAsync(nonAdmin);
        await _ctx.SaveChangesAsync();

        var count = await _sut.CountAdminsAsync(adminOrgId, adminRoleId);
        count.Should().Be(2);
    }
}
