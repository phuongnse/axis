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
public class TenantRegistrationTokenStoreTests(IdentityDatabaseFixture db) : IAsyncLifetime
{
    private IdentityDbContext _ctx = null!;
    private TenantRegistrationTokenStore _sut = null!;

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new TenantRegistrationTokenStore(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task CreateFirstUserSetupAsync_BeforeUnitOfWorkSave_DoesNotPersistToken()
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        Tenant Tenant = Tenant.RegisterForContactVerification(
            "Acme",
            TenantSlug.Create($"acme-{suffix}").Value!,
            Email.Create($"admin-{suffix}@acme.test").Value!,
            WellKnownSubscriptionPlans.FreeId,
            WellKnownLegalDocuments.TermsVersion,
            WellKnownLegalDocuments.PrivacyVersion);
        _ctx.Tenants.Add(Tenant);
        await _ctx.SaveChangesAsync();

        string tokenHash = OpaqueTokenGenerator.Hash($"setup-{Guid.NewGuid():N}");

        await _sut.CreateFirstUserSetupAsync(
            Tenant.Id,
            tokenHash,
            DateTime.UtcNow.AddHours(1));

        await using IdentityDbContext readBeforeSave = db.CreateContext();
        bool tokenPersistedBeforeSave = await readBeforeSave.Set<TenantRegistrationToken>()
            .AnyAsync(t => t.TokenHash == tokenHash);
        tokenPersistedBeforeSave.Should().BeFalse();

        await _ctx.SaveChangesAsync();

        await using IdentityDbContext readAfterSave = db.CreateContext();
        bool tokenPersistedAfterSave = await readAfterSave.Set<TenantRegistrationToken>()
            .AnyAsync(t => t.TokenHash == tokenHash);
        tokenPersistedAfterSave.Should().BeTrue();
    }
}
