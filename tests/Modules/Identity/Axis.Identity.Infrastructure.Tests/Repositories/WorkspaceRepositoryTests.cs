using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

[Collection("IdentityDb")]
public class WorkspaceRepositoryTests(IdentityDatabaseFixture db) : IAsyncLifetime
{
    private IdentityDbContext _ctx = null!;
    private WorkspaceRepository _sut = null!;

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new WorkspaceRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static Workspace MakeWorkspace(string slug = "test-workspace") =>
        Workspace.Create(
            "Test Workspace",
            WorkspaceSlug.Create(slug).Value,
            Email.Create("owner@example.com").Value);

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        Workspace workspace = MakeWorkspace("workspace-add-get");
        await _sut.AddAsync(workspace);
        await _ctx.SaveChangesAsync();
        Workspace? loaded = await _sut.GetByIdAsync(workspace.Id);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be(workspace.Name);
        loaded.Slug.Value.Should().Be("workspace-add-get");
        loaded.OwnerEmail.Value.Should().Be("owner@example.com");
        loaded.Status.Should().Be(WorkspaceStatus.PendingVerification);
    }

    [Fact]
    public async Task GetBySlugAsync_WhenSlugExists_ReturnsMatchingWorkspace()
    {
        Workspace workspace = MakeWorkspace("workspace-by-slug");
        await _sut.AddAsync(workspace);
        await _ctx.SaveChangesAsync();
        WorkspaceSlug slug = WorkspaceSlug.Create("workspace-by-slug").Value;
        Workspace? loaded = await _sut.GetBySlugAsync(slug);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(workspace.Id);
    }

    [Fact]
    public async Task GetPersonalByOwnerUserIdAsync_WhenPersonalWorkspaceExists_ReturnsOwnedWorkspace()
    {
        Guid ownerUserId = Guid.NewGuid();
        Workspace workspace = Workspace.CreatePersonal(
            "Jane Doe",
            WorkspaceSlug.Create("jane-doe").Value,
            Email.Create("jane@example.com").Value,
            ownerUserId);
        await _sut.AddAsync(workspace);
        await _ctx.SaveChangesAsync();

        Workspace? loaded = await _sut.GetPersonalByOwnerUserIdAsync(ownerUserId);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(workspace.Id);
    }

    [Fact]
    public async Task GetBySlugAsync_WhenSlugDoesNotExist_ReturnsNull()
    {
        WorkspaceSlug slug = WorkspaceSlug.Create("does-not-exist").Value;
        Workspace? result = await _sut.GetBySlugAsync(slug);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SlugExistsAsync_WhenSlugIsTaken_ReturnsTrue()
    {
        Workspace workspace = MakeWorkspace("slug-exists-check");
        await _sut.AddAsync(workspace);
        await _ctx.SaveChangesAsync();
        bool exists = await _sut.SlugExistsAsync(WorkspaceSlug.Create("slug-exists-check").Value);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SlugExistsAsync_WhenSlugIsUnused_ReturnsFalse()
    {
        bool exists = await _sut.SlugExistsAsync(WorkspaceSlug.Create("never-used-slug").Value);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdAsync_WhenIdDoesNotExist_ReturnsNull()
    {
        Workspace? result = await _sut.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }
}
