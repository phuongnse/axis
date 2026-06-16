using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class TenantTests
{
    private static TenantSlug ValidSlug => TenantSlug.Create("acme-corp").Value;
    private static Email ValidEmail => Email.Create("admin@acme.com").Value;

    [Fact]
    public void Tenant_WhenCreated_ProducesValidTenant()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Tenant.Name.Should().Be("Acme Corp");
        Tenant.Slug.Should().Be(ValidSlug);
        Tenant.OwnerEmail.Should().Be(ValidEmail);
        Tenant.Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public void Tenant_WhenCreated_RaisesTenantCreatedEvent()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Tenant.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TenantCreated>();
    }

    [Fact]
    public void Tenant_WhenRegisteredForContactVerification_IsPendingAndRecordsLegalVersions()
    {
        Tenant Tenant = Tenant.RegisterForContactVerification(
            "Acme Corp",
            ValidSlug,
            ValidEmail,
            WellKnownSubscriptionPlans.FreeId,
            WellKnownLegalDocuments.TermsVersion,
            WellKnownLegalDocuments.PrivacyVersion);

        Tenant.Status.Should().Be(TenantStatus.PendingVerification);
        Tenant.AcceptedTermsVersion.Should().Be(WellKnownLegalDocuments.TermsVersion);
        Tenant.AcceptedPrivacyVersion.Should().Be(WellKnownLegalDocuments.PrivacyVersion);
        Tenant.LegalAcceptedAt.Should().NotBeNull();
    }

    [Fact]
    public void Tenant_WhenCreated_TenantCreatedEventContainsCorrectData()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        TenantCreated evt = Tenant.DomainEvents.OfType<TenantCreated>().Single();
        evt.tenantId.Should().Be(Tenant.Id);
        evt.Name.Should().Be("Acme Corp");
        evt.Slug.Should().Be("acme-corp");
        evt.OwnerEmail.Should().Be("admin@acme.com");
    }

    [Fact]
    public void Tenant_WhenArchived_ChangesStatusToArchived()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Tenant.ClearDomainEvents();

        Tenant.Archive();

        Tenant.Status.Should().Be(TenantStatus.Archived);
    }

    [Fact]
    public void Tenant_WhenAlreadyArchived_ArchiveThrows()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Tenant.Archive();

        Action act = () => Tenant.Archive();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already archived*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Tenant_WhenCreatedWithEmptyName_Throws(string name)
    {
        Action act = () => Tenant.Create(name, ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BeginProvisioning_WhenProvisioningFailed_AllowsRetry()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Tenant.BeginProvisioning();
        Tenant.MarkProvisioningFailed();

        Tenant.BeginProvisioning();

        Tenant.Status.Should().Be(TenantStatus.Provisioning);
    }

    [Fact]
    public void BeginProvisioningAfterContactVerification_WhenAlreadyProvisioning_DoesNotRaiseDuplicateEvent()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Tenant.BeginProvisioning();
        Tenant.ClearDomainEvents();

        Tenant.BeginProvisioningAfterContactVerification();

        Tenant.Status.Should().Be(TenantStatus.Provisioning);
        Tenant.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MarkProvisioningFailed_WhenNotProvisioning_Throws()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Action act = () => Tenant.MarkProvisioningFailed();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only provisioning Tenants*");
    }

    [Fact]
    public void UpdateProfile_WhenValid_UpdatesNameTimezoneAndLanguage()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Tenant.UpdateProfile("New Name", "Europe/Berlin", "de-DE");

        Tenant.Name.Should().Be("New Name");
        Tenant.TimeZoneId.Should().Be("Europe/Berlin");
        Tenant.DefaultLanguage.Should().Be("de-DE");
        Tenant.Slug.Should().Be(ValidSlug);
    }

    [Fact]
    public void UpdateProfile_WhenNameTooShort_Throws()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Action act = () => Tenant.UpdateProfile("A", null, null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateLogoUrl_WhenDeleted_Throws()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Tenant.MarkDeleted();

        Action act = () => Tenant.UpdateLogoUrl("https://example.com/logo.png");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ScheduleDeletion_WhenActive_SetsDeletionScheduledAndGraceDate()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        DateTime now = new(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);

        Tenant.ScheduleDeletion(now);

        Tenant.Status.Should().Be(TenantStatus.DeletionScheduled);
        Tenant.ScheduledHardDeleteAt.Should().Be(now.AddDays(Tenant.DeletionGracePeriodDays));
    }

    [Fact]
    public void CancelScheduledDeletion_WhenScheduled_RestoresActive()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Tenant.ScheduleDeletion(DateTime.UtcNow);

        Tenant.CancelScheduledDeletion();

        Tenant.Status.Should().Be(TenantStatus.Active);
        Tenant.ScheduledHardDeleteAt.Should().BeNull();
    }

    [Fact]
    public void MarkProvisioningFailed_WhenAlreadyFailed_IsIdempotent()
    {
        Tenant Tenant = Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Tenant.BeginProvisioning();
        Tenant.MarkProvisioningFailed();

        Tenant.MarkProvisioningFailed();

        Tenant.Status.Should().Be(TenantStatus.ProvisioningFailed);
    }

    [Theory]
    [InlineData(TenantStatus.Active, true)]
    [InlineData(TenantStatus.DeletionScheduled, true)]
    [InlineData(TenantStatus.PendingVerification, false)]
    [InlineData(TenantStatus.Provisioning, false)]
    [InlineData(TenantStatus.ProvisioningFailed, false)]
    [InlineData(TenantStatus.Deleted, false)]
    [InlineData(TenantStatus.Archived, false)]
    public void AllowsTenantDataAccess_WhenStatusVaries_MatchesPolicy(
        TenantStatus status,
        bool expected)
    {
        Tenant Tenant = status == TenantStatus.PendingVerification
            ? Tenant.RegisterForContactVerification(
                "Acme Corp",
                ValidSlug,
                ValidEmail,
                WellKnownSubscriptionPlans.FreeId,
                WellKnownLegalDocuments.TermsVersion,
                WellKnownLegalDocuments.PrivacyVersion)
            : Tenant.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        if (status != TenantStatus.PendingVerification)
            Tenant.BeginProvisioning();

        switch (status)
        {
            case TenantStatus.PendingVerification:
                break;
            case TenantStatus.Active:
                Tenant.CompleteProvisioning();
                break;
            case TenantStatus.DeletionScheduled:
                Tenant.CompleteProvisioning();
                Tenant.ScheduleDeletion(DateTime.UtcNow);
                break;
            case TenantStatus.Provisioning:
                break;
            case TenantStatus.ProvisioningFailed:
                Tenant.MarkProvisioningFailed();
                break;
            case TenantStatus.Deleted:
                Tenant.CompleteProvisioning();
                Tenant.MarkDeleted();
                break;
            case TenantStatus.Archived:
                Tenant.CompleteProvisioning();
                Tenant.Archive();
                break;
        }

        Tenant.AllowsTenantDataAccess().Should().Be(expected);
    }
}
