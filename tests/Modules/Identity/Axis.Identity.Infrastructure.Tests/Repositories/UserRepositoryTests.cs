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

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new UserRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static User MakeUser(string email) =>
        User.Create("Jane Doe", Email.Create(email).Value);

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        User user = MakeUser("getbyid@example.com");
        await _sut.AddAsync(user);
        await _ctx.SaveChangesAsync();
        User? loaded = await _sut.GetByIdPlatformWideAsync(user.Id);

        loaded.Should().NotBeNull();
        loaded!.Email.Value.Should().Be("getbyid@example.com");
        loaded.FullName.Should().Be("Jane Doe");
        loaded.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public async Task EmailExistsPlatformWideAsync_WhenEmailExistsInAnyWorkspace_ReturnsTrue()
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
    public async Task AddAsync_WhenUserHasPasswordHash_PersistsAndReloadsPasswordHash()
    {
        User user = MakeUser("withhash@example.com");
        user.SetPasswordHash("$2a$12$fakehashvalue");
        await _sut.AddAsync(user);
        await _ctx.SaveChangesAsync();
        User? loaded = await _sut.GetByIdPlatformWideAsync(user.Id);

        loaded!.PasswordHash.Should().Be("$2a$12$fakehashvalue");
    }

    [Fact]
    public async Task FindByEmailGloballyAsync_WhenEmailExistsInAnyWorkspace_ReturnsUser()
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
