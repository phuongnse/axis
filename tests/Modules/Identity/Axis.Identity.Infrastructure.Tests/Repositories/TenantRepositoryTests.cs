using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

[Collection("IdentityDb")]
public class TenantRepositoryTests(IdentityDatabaseFixture db) : IAsyncLifetime
{
    private IdentityDbContext _ctx = null!;
    private TenantRepository _sut = null!;

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new TenantRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static Tenant MakeTenant(string slug = "test-tenant") =>
        Tenant.Create(
            "Test Tenant",
            TenantSlug.Create(slug).Value,
            Email.Create("owner@example.com").Value,
            WellKnownSubscriptionPlans.FreeId);

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        Tenant tenant = MakeTenant("tenant-add-get");
        await _sut.AddAsync(tenant);
        await _ctx.SaveChangesAsync();
        Tenant? loaded = await _sut.GetByIdAsync(tenant.Id);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be(tenant.Name);
        loaded.Slug.Value.Should().Be("tenant-add-get");
        loaded.OwnerEmail.Value.Should().Be("owner@example.com");
        loaded.Status.Should().Be(TenantStatus.Active);
        loaded.SubscriptionPlanId.Should().Be(WellKnownSubscriptionPlans.FreeId);
    }

    [Fact]
    public async Task GetBySlugAsync_WhenSlugExists_ReturnsMatchingTenant()
    {
        Tenant tenant = MakeTenant("tenant-by-slug");
        await _sut.AddAsync(tenant);
        await _ctx.SaveChangesAsync();
        TenantSlug slug = TenantSlug.Create("tenant-by-slug").Value;
        Tenant? loaded = await _sut.GetBySlugAsync(slug);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(tenant.Id);
    }

    [Fact]
    public async Task GetBySlugAsync_WhenSlugDoesNotExist_ReturnsNull()
    {
        TenantSlug slug = TenantSlug.Create("does-not-exist").Value;
        Tenant? result = await _sut.GetBySlugAsync(slug);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SlugExistsAsync_WhenSlugIsTaken_ReturnsTrue()
    {
        Tenant tenant = MakeTenant("slug-exists-check");
        await _sut.AddAsync(tenant);
        await _ctx.SaveChangesAsync();
        bool exists = await _sut.SlugExistsAsync(TenantSlug.Create("slug-exists-check").Value);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SlugExistsAsync_WhenSlugIsUnused_ReturnsFalse()
    {
        bool exists = await _sut.SlugExistsAsync(TenantSlug.Create("never-used-slug").Value);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdAsync_WhenIdDoesNotExist_ReturnsNull()
    {
        Tenant? result = await _sut.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }
}
