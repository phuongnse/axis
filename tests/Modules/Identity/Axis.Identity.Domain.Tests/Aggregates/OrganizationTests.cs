using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class OrganizationTests
{
    private static OrganizationSlug ValidSlug => OrganizationSlug.Create("acme-corp").Value;
    private static Email ValidEmail => Email.Create("admin@acme.com").Value;

    [Fact]
    public void Organization_WhenCreated_ProducesValidOrganization()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        org.Name.Should().Be("Acme Corp");
        org.Slug.Should().Be(ValidSlug);
        org.OwnerEmail.Should().Be(ValidEmail);
        org.Status.Should().Be(OrganizationStatus.Active);
    }

    [Fact]
    public void Organization_WhenCreated_RaisesOrganizationCreatedEvent()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        org.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrganizationCreated>();
    }

    [Fact]
    public void Organization_WhenRegisteredForContactVerification_IsPendingAndRecordsLegalVersions()
    {
        Organization org = Organization.RegisterForContactVerification(
            "Acme Corp",
            ValidSlug,
            ValidEmail,
            WellKnownSubscriptionPlans.FreeId,
            WellKnownLegalDocuments.TermsVersion,
            WellKnownLegalDocuments.PrivacyVersion);

        org.Status.Should().Be(OrganizationStatus.PendingVerification);
        org.AcceptedTermsVersion.Should().Be(WellKnownLegalDocuments.TermsVersion);
        org.AcceptedPrivacyVersion.Should().Be(WellKnownLegalDocuments.PrivacyVersion);
        org.LegalAcceptedAt.Should().NotBeNull();
    }

    [Fact]
    public void Organization_WhenCreated_OrganizationCreatedEventContainsCorrectData()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        OrganizationCreated evt = org.DomainEvents.OfType<OrganizationCreated>().Single();
        evt.OrganizationId.Should().Be(org.Id);
        evt.Name.Should().Be("Acme Corp");
        evt.Slug.Should().Be("acme-corp");
        evt.OwnerEmail.Should().Be("admin@acme.com");
    }

    [Fact]
    public void Organization_WhenArchived_ChangesStatusToArchived()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        org.ClearDomainEvents();

        org.Archive();

        org.Status.Should().Be(OrganizationStatus.Archived);
    }

    [Fact]
    public void Organization_WhenAlreadyArchived_ArchiveThrows()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        org.Archive();

        Action act = () => org.Archive();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already archived*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Organization_WhenCreatedWithEmptyName_Throws(string name)
    {
        Action act = () => Organization.Create(name, ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BeginProvisioning_WhenProvisioningFailed_AllowsRetry()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        org.BeginProvisioning();
        org.MarkProvisioningFailed();

        org.BeginProvisioning();

        org.Status.Should().Be(OrganizationStatus.Provisioning);
    }

    [Fact]
    public void BeginProvisioningAfterContactVerification_WhenAlreadyProvisioning_DoesNotRaiseDuplicateEvent()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        org.BeginProvisioning();
        org.ClearDomainEvents();

        org.BeginProvisioningAfterContactVerification();

        org.Status.Should().Be(OrganizationStatus.Provisioning);
        org.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MarkProvisioningFailed_WhenNotProvisioning_Throws()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Action act = () => org.MarkProvisioningFailed();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only provisioning organizations*");
    }

    [Fact]
    public void UpdateProfile_WhenValid_UpdatesNameTimezoneAndLanguage()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        org.UpdateProfile("New Name", "Europe/Berlin", "de-DE");

        org.Name.Should().Be("New Name");
        org.TimeZoneId.Should().Be("Europe/Berlin");
        org.DefaultLanguage.Should().Be("de-DE");
        org.Slug.Should().Be(ValidSlug);
    }

    [Fact]
    public void UpdateProfile_WhenNameTooShort_Throws()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Action act = () => org.UpdateProfile("A", null, null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateLogoUrl_WhenDeleted_Throws()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        org.MarkDeleted();

        Action act = () => org.UpdateLogoUrl("https://example.com/logo.png");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ScheduleDeletion_WhenActive_SetsDeletionScheduledAndGraceDate()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        DateTime now = new(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);

        org.ScheduleDeletion(now);

        org.Status.Should().Be(OrganizationStatus.DeletionScheduled);
        org.ScheduledHardDeleteAt.Should().Be(now.AddDays(Organization.DeletionGracePeriodDays));
    }

    [Fact]
    public void CancelScheduledDeletion_WhenScheduled_RestoresActive()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        org.ScheduleDeletion(DateTime.UtcNow);

        org.CancelScheduledDeletion();

        org.Status.Should().Be(OrganizationStatus.Active);
        org.ScheduledHardDeleteAt.Should().BeNull();
    }

    [Fact]
    public void MarkProvisioningFailed_WhenAlreadyFailed_IsIdempotent()
    {
        Organization org = Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        org.BeginProvisioning();
        org.MarkProvisioningFailed();

        org.MarkProvisioningFailed();

        org.Status.Should().Be(OrganizationStatus.ProvisioningFailed);
    }

    [Theory]
    [InlineData(OrganizationStatus.Active, true)]
    [InlineData(OrganizationStatus.DeletionScheduled, true)]
    [InlineData(OrganizationStatus.PendingVerification, false)]
    [InlineData(OrganizationStatus.Provisioning, false)]
    [InlineData(OrganizationStatus.ProvisioningFailed, false)]
    [InlineData(OrganizationStatus.Deleted, false)]
    [InlineData(OrganizationStatus.Archived, false)]
    public void AllowsTenantDataAccess_WhenStatusVaries_MatchesPolicy(
        OrganizationStatus status,
        bool expected)
    {
        Organization org = status == OrganizationStatus.PendingVerification
            ? Organization.RegisterForContactVerification(
                "Acme Corp",
                ValidSlug,
                ValidEmail,
                WellKnownSubscriptionPlans.FreeId,
                WellKnownLegalDocuments.TermsVersion,
                WellKnownLegalDocuments.PrivacyVersion)
            : Organization.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        if (status != OrganizationStatus.PendingVerification)
            org.BeginProvisioning();

        switch (status)
        {
            case OrganizationStatus.PendingVerification:
                break;
            case OrganizationStatus.Active:
                org.CompleteProvisioning();
                break;
            case OrganizationStatus.DeletionScheduled:
                org.CompleteProvisioning();
                org.ScheduleDeletion(DateTime.UtcNow);
                break;
            case OrganizationStatus.Provisioning:
                break;
            case OrganizationStatus.ProvisioningFailed:
                org.MarkProvisioningFailed();
                break;
            case OrganizationStatus.Deleted:
                org.CompleteProvisioning();
                org.MarkDeleted();
                break;
            case OrganizationStatus.Archived:
                org.CompleteProvisioning();
                org.Archive();
                break;
        }

        org.AllowsTenantDataAccess().Should().Be(expected);
    }
}
