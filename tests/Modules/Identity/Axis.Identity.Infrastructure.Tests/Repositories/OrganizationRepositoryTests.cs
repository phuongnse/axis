using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

[Collection("IdentityDb")]
public class OrganizationRepositoryTests(IdentityDatabaseFixture db) : IAsyncLifetime
{
    private IdentityDbContext _ctx = null!;
    private OrganizationRepository _sut = null!;

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new OrganizationRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static Organization MakeOrg(string slug = "test-org") =>
        Organization.Create(
            "Test Org",
            OrganizationSlug.Create(slug).Value,
            Email.Create("owner@example.com").Value);

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        var org = MakeOrg("org-add-get");
        await _sut.AddAsync(org);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(org.Id);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be(org.Name);
        loaded.Slug.Value.Should().Be("org-add-get");
        loaded.OwnerEmail.Value.Should().Be("owner@example.com");
        loaded.Status.Should().Be(OrganizationStatus.Active);
    }

    [Fact]
    public async Task GetBySlugAsync_WhenSlugExists_ReturnsMatchingOrg()
    {
        var org = MakeOrg("org-by-slug");
        await _sut.AddAsync(org);
        await _ctx.SaveChangesAsync();

        var slug = OrganizationSlug.Create("org-by-slug").Value;
        var loaded = await _sut.GetBySlugAsync(slug);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(org.Id);
    }

    [Fact]
    public async Task GetBySlugAsync_WhenSlugDoesNotExist_ReturnsNull()
    {
        var slug = OrganizationSlug.Create("does-not-exist").Value;
        var result = await _sut.GetBySlugAsync(slug);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SlugExistsAsync_WhenSlugIsTaken_ReturnsTrue()
    {
        var org = MakeOrg("slug-exists-check");
        await _sut.AddAsync(org);
        await _ctx.SaveChangesAsync();

        var exists = await _sut.SlugExistsAsync(OrganizationSlug.Create("slug-exists-check").Value);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SlugExistsAsync_WhenSlugIsUnused_ReturnsFalse()
    {
        var exists = await _sut.SlugExistsAsync(OrganizationSlug.Create("never-used-slug").Value);
        exists.Should().BeFalse();
    }
}
