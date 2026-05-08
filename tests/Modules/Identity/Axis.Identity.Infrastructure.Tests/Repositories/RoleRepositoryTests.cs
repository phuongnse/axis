using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

[Collection("IdentityDb")]
public class RoleRepositoryTests(IdentityDatabaseFixture db) : IAsyncLifetime
{
    private IdentityDbContext _ctx = null!;
    private RoleRepository _sut = null!;

    private static readonly Guid OrgId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new RoleRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static Role MakeRole(string name = "Editor") =>
        Role.Create(name, null, OrgId, ["data_modeling:model:read"]);

    [Fact]
    public async Task AddAsync_and_GetByIdAsync_round_trip()
    {
        var role = MakeRole("CustomRole-GetById");
        await _sut.AddAsync(role);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(role.Id, OrgId);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("CustomRole-GetById");
        loaded.IsSystem.Should().BeFalse();
        loaded.Permissions.Should().ContainSingle().Which.Should().Be("data_modeling:model:read");
    }

    [Fact]
    public async Task GetByNameAsync_returns_matching_role()
    {
        var role = MakeRole("NamedRole-FindMe");
        await _sut.AddAsync(role);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByNameAsync("NamedRole-FindMe", OrgId);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(role.Id);
    }

    [Fact]
    public async Task GetAllAsync_returns_all_roles_for_org()
    {
        var r1 = MakeRole($"BulkRole-A-{Guid.NewGuid():N}");
        var r2 = MakeRole($"BulkRole-B-{Guid.NewGuid():N}");
        await _sut.AddAsync(r1);
        await _sut.AddAsync(r2);
        await _ctx.SaveChangesAsync();

        var all = await _sut.GetAllAsync(OrgId);

        all.Should().Contain(r => r.Id == r1.Id);
        all.Should().Contain(r => r.Id == r2.Id);
    }

    [Fact]
    public async Task NameExistsAsync_returns_true_for_existing_name()
    {
        var role = MakeRole($"DupeName-{Guid.NewGuid():N}");
        await _sut.AddAsync(role);
        await _ctx.SaveChangesAsync();

        var exists = await _sut.NameExistsAsync(role.Name, OrgId);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task NameExistsAsync_excludes_specified_role_id()
    {
        var role = MakeRole($"ExcludeSelf-{Guid.NewGuid():N}");
        await _sut.AddAsync(role);
        await _ctx.SaveChangesAsync();

        var exists = await _sut.NameExistsAsync(role.Name, OrgId, excludeRoleId: role.Id);
        exists.Should().BeFalse();
    }
}
