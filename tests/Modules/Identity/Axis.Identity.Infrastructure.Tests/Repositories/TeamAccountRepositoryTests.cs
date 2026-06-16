using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

[Collection("IdentityDb")]
public class TeamAccountRepositoryTests(IdentityDatabaseFixture db) : IAsyncLifetime
{
    private IdentityDbContext _ctx = null!;
    private TeamAccountRepository _sut = null!;

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new TeamAccountRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static TeamAccount MakeTeamAccount(string slug = "test-team-account") =>
        TeamAccount.Create(
            "Test Team Account",
            TeamAccountSlug.Create(slug).Value,
            Email.Create("owner@example.com").Value,
            WellKnownSubscriptionPlans.FreeId);

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        TeamAccount teamAccount = MakeTeamAccount("team-account-add-get");
        await _sut.AddAsync(teamAccount);
        await _ctx.SaveChangesAsync();
        TeamAccount? loaded = await _sut.GetByIdAsync(teamAccount.Id);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be(teamAccount.Name);
        loaded.Slug.Value.Should().Be("team-account-add-get");
        loaded.OwnerEmail.Value.Should().Be("owner@example.com");
        loaded.Status.Should().Be(TeamAccountStatus.Active);
        loaded.SubscriptionPlanId.Should().Be(WellKnownSubscriptionPlans.FreeId);
    }

    [Fact]
    public async Task GetBySlugAsync_WhenSlugExists_ReturnsMatchingTeamAccount()
    {
        TeamAccount teamAccount = MakeTeamAccount("team-account-by-slug");
        await _sut.AddAsync(teamAccount);
        await _ctx.SaveChangesAsync();
        TeamAccountSlug slug = TeamAccountSlug.Create("team-account-by-slug").Value;
        TeamAccount? loaded = await _sut.GetBySlugAsync(slug);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(teamAccount.Id);
    }

    [Fact]
    public async Task GetBySlugAsync_WhenSlugDoesNotExist_ReturnsNull()
    {
        TeamAccountSlug slug = TeamAccountSlug.Create("does-not-exist").Value;
        TeamAccount? result = await _sut.GetBySlugAsync(slug);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SlugExistsAsync_WhenSlugIsTaken_ReturnsTrue()
    {
        TeamAccount teamAccount = MakeTeamAccount("slug-exists-check");
        await _sut.AddAsync(teamAccount);
        await _ctx.SaveChangesAsync();
        bool exists = await _sut.SlugExistsAsync(TeamAccountSlug.Create("slug-exists-check").Value);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SlugExistsAsync_WhenSlugIsUnused_ReturnsFalse()
    {
        bool exists = await _sut.SlugExistsAsync(TeamAccountSlug.Create("never-used-slug").Value);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdAsync_WhenIdDoesNotExist_ReturnsNull()
    {
        TeamAccount? result = await _sut.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }
}
