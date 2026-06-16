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

    private static readonly Guid TenantId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new RoleRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static Role MakeRole(string name = "Editor") =>
        Role.Create(name, null, TenantId, ["data_modeling:model:read"]);

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        Role role = MakeRole("CustomRole-GetById");
        await _sut.AddAsync(role);
        await _ctx.SaveChangesAsync();
        Role? loaded = await _sut.GetByIdAsync(role.Id, TenantId);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("CustomRole-GetById");
        loaded.IsSystem.Should().BeFalse();
        loaded.Permissions.Should().ContainSingle().Which.Should().Be("data_modeling:model:read");
    }

    [Fact]
    public async Task GetByNameAsync_WhenNameExists_ReturnsMatchingRole()
    {
        Role role = MakeRole("NamedRole-FindMe");
        await _sut.AddAsync(role);
        await _ctx.SaveChangesAsync();
        Role? loaded = await _sut.GetByNameAsync("NamedRole-FindMe", TenantId);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(role.Id);
    }

    [Fact]
    public async Task GetAllAsync_WhenMultipleRolesExist_ReturnsAllRolesForTenant()
    {
        Role r1 = MakeRole($"BulkRole-A-{Guid.NewGuid():N}");
        Role r2 = MakeRole($"BulkRole-B-{Guid.NewGuid():N}");
        await _sut.AddAsync(r1);
        await _sut.AddAsync(r2);
        await _ctx.SaveChangesAsync();
        IReadOnlyList<Role> all = await _sut.GetAllAsync(TenantId);

        all.Should().Contain(r => r.Id == r1.Id);
        all.Should().Contain(r => r.Id == r2.Id);
    }

    [Fact]
    public async Task NameExistsAsync_WhenNameExistsInTenant_ReturnsTrue()
    {
        Role role = MakeRole($"DupeName-{Guid.NewGuid():N}");
        await _sut.AddAsync(role);
        await _ctx.SaveChangesAsync();
        bool exists = await _sut.NameExistsAsync(role.Name, TenantId);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task NameExistsAsync_WhenExcludeRoleIdSpecified_ExcludesThatRoleFromCheck()
    {
        Role role = MakeRole($"ExcludeSelf-{Guid.NewGuid():N}");
        await _sut.AddAsync(role);
        await _ctx.SaveChangesAsync();

        bool exists = await _sut.NameExistsAsync(role.Name, TenantId, excludeRoleId: role.Id);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdAsync_WhenRoleBelongsToDifferentTenant_ReturnsNull()
    {
        Role role = MakeRole($"CrossTenant-{Guid.NewGuid():N}");
        await _sut.AddAsync(role);
        await _ctx.SaveChangesAsync();

        Role? result = await _sut.GetByIdAsync(role.Id, Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdsAsync_WhenIdsProvided_ReturnsMatchingRolesForTenant()
    {
        Guid TenantId = Guid.NewGuid();
        Role r1 = Role.Create($"GetByIds-A-{Guid.NewGuid():N}", null, TenantId, ["data_modeling:model:read"]);
        Role r2 = Role.Create($"GetByIds-B-{Guid.NewGuid():N}", null, TenantId, ["data_modeling:model:read"]);
        await _sut.AddAsync(r1);
        await _sut.AddAsync(r2);
        await _ctx.SaveChangesAsync();

        IReadOnlyList<Role> result = await _sut.GetByIdsAsync([r1.Id, r2.Id], TenantId);

        result.Should().HaveCount(2);
        result.Select(r => r.Id).Should().BeEquivalentTo(new[] { r1.Id, r2.Id });
    }

    [Fact]
    public async Task GetByIdsAsync_WhenIdsBelongToDifferentTenant_ReturnsEmpty()
    {
        Role role = MakeRole($"GetByIds-CrossTenant-{Guid.NewGuid():N}");
        await _sut.AddAsync(role);
        await _ctx.SaveChangesAsync();

        IReadOnlyList<Role> result = await _sut.GetByIdsAsync([role.Id], Guid.NewGuid());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPagedAsync_WhenRolesExist_ReturnsPagedResult()
    {
        Guid TenantId = Guid.NewGuid();
        Role r1 = Role.Create($"Paged-A-{Guid.NewGuid():N}", null, TenantId, ["data_modeling:model:read"]);
        Role r2 = Role.Create($"Paged-B-{Guid.NewGuid():N}", null, TenantId, ["data_modeling:model:read"]);
        await _sut.AddAsync(r1);
        await _sut.AddAsync(r2);
        await _ctx.SaveChangesAsync();

        (IReadOnlyList<Role> items, int total) = await _sut.GetPagedAsync(TenantId, 1, 20);

        items.Should().HaveCount(2);
        total.Should().Be(2);
    }
}
