using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Tests.Services;

[Collection("IdentityDb")]
public class OrganizationRegistrationTokenStoreTests(IdentityDatabaseFixture db) : IAsyncLifetime
{
    private IdentityDbContext _ctx = null!;
    private OrganizationRegistrationTokenStore _sut = null!;

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new OrganizationRegistrationTokenStore(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task CreateFirstUserSetupAsync_DoesNotPersistBeforeUnitOfWorkSave()
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        Organization org = Organization.RegisterForContactVerification(
            "Acme",
            OrganizationSlug.Create($"acme-{suffix}").Value!,
            Email.Create($"admin-{suffix}@acme.test").Value!,
            WellKnownSubscriptionPlans.FreeId,
            WellKnownLegalDocuments.TermsVersion,
            WellKnownLegalDocuments.PrivacyVersion);
        _ctx.Organizations.Add(org);
        await _ctx.SaveChangesAsync();

        string tokenHash = OpaqueTokenGenerator.Hash($"setup-{Guid.NewGuid():N}");

        await _sut.CreateFirstUserSetupAsync(
            org.Id,
            tokenHash,
            DateTime.UtcNow.AddHours(1));

        await using IdentityDbContext readBeforeSave = db.CreateContext();
        bool tokenPersistedBeforeSave = await readBeforeSave.Set<OrganizationRegistrationToken>()
            .AnyAsync(t => t.TokenHash == tokenHash);
        tokenPersistedBeforeSave.Should().BeFalse();

        await _ctx.SaveChangesAsync();

        await using IdentityDbContext readAfterSave = db.CreateContext();
        bool tokenPersistedAfterSave = await readAfterSave.Set<OrganizationRegistrationToken>()
            .AnyAsync(t => t.TokenHash == tokenHash);
        tokenPersistedAfterSave.Should().BeTrue();
    }
}
