using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class TeamAccountTests
{
    private static TeamAccountSlug ValidSlug => TeamAccountSlug.Create("acme-corp").Value;
    private static Email ValidEmail => Email.Create("admin@acme.com").Value;

    [Fact]
    public void TeamAccount_WhenCreated_ProducesValidTeamAccount()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        teamAccount.Name.Should().Be("Acme Corp");
        teamAccount.Slug.Should().Be(ValidSlug);
        teamAccount.OwnerEmail.Should().Be(ValidEmail);
        teamAccount.Status.Should().Be(TeamAccountStatus.Active);
    }

    [Fact]
    public void TeamAccount_WhenCreated_RaisesTeamAccountCreatedEvent()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        teamAccount.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TeamAccountCreated>();
    }

    [Fact]
    public void TeamAccount_WhenRegisteredForContactVerification_IsPendingAndRecordsLegalVersions()
    {
        TeamAccount teamAccount = TeamAccount.RegisterForContactVerification(
            "Acme Corp",
            ValidSlug,
            ValidEmail,
            WellKnownSubscriptionPlans.FreeId,
            WellKnownLegalDocuments.TermsVersion,
            WellKnownLegalDocuments.PrivacyVersion);

        teamAccount.Status.Should().Be(TeamAccountStatus.PendingVerification);
        teamAccount.AcceptedTermsVersion.Should().Be(WellKnownLegalDocuments.TermsVersion);
        teamAccount.AcceptedPrivacyVersion.Should().Be(WellKnownLegalDocuments.PrivacyVersion);
        teamAccount.LegalAcceptedAt.Should().NotBeNull();
    }

    [Fact]
    public void TeamAccount_WhenCreated_TeamAccountCreatedEventContainsCorrectData()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        TeamAccountCreated evt = teamAccount.DomainEvents.OfType<TeamAccountCreated>().Single();
        evt.TeamAccountId.Should().Be(teamAccount.Id);
        evt.Name.Should().Be("Acme Corp");
        evt.Slug.Should().Be("acme-corp");
        evt.OwnerEmail.Should().Be("admin@acme.com");
    }

    [Fact]
    public void TeamAccount_WhenArchived_ChangesStatusToArchived()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        teamAccount.ClearDomainEvents();

        teamAccount.Archive();

        teamAccount.Status.Should().Be(TeamAccountStatus.Archived);
    }

    [Fact]
    public void TeamAccount_WhenAlreadyArchived_ArchiveThrows()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        teamAccount.Archive();

        Action act = () => teamAccount.Archive();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already archived*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TeamAccount_WhenCreatedWithEmptyName_Throws(string name)
    {
        Action act = () => TeamAccount.Create(name, ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BeginProvisioning_WhenProvisioningFailed_AllowsRetry()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        teamAccount.BeginProvisioning();
        teamAccount.MarkProvisioningFailed();

        teamAccount.BeginProvisioning();

        teamAccount.Status.Should().Be(TeamAccountStatus.Provisioning);
    }

    [Fact]
    public void BeginProvisioningAfterContactVerification_WhenAlreadyProvisioning_DoesNotRaiseDuplicateEvent()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        teamAccount.BeginProvisioning();
        teamAccount.ClearDomainEvents();

        teamAccount.BeginProvisioningAfterContactVerification();

        teamAccount.Status.Should().Be(TeamAccountStatus.Provisioning);
        teamAccount.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MarkProvisioningFailed_WhenNotProvisioning_Throws()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Action act = () => teamAccount.MarkProvisioningFailed();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only provisioning team accounts*");
    }

    [Fact]
    public void UpdateProfile_WhenValid_UpdatesNameTimezoneAndLanguage()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        teamAccount.UpdateProfile("New Name", "Europe/Berlin", "de-DE");

        teamAccount.Name.Should().Be("New Name");
        teamAccount.TimeZoneId.Should().Be("Europe/Berlin");
        teamAccount.DefaultLanguage.Should().Be("de-DE");
        teamAccount.Slug.Should().Be(ValidSlug);
    }

    [Fact]
    public void UpdateProfile_WhenNameTooShort_Throws()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Action act = () => teamAccount.UpdateProfile("A", null, null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateLogoUrl_WhenDeleted_Throws()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        teamAccount.MarkDeleted();

        Action act = () => teamAccount.UpdateLogoUrl("https://example.com/logo.png");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ScheduleDeletion_WhenActive_SetsDeletionScheduledAndGraceDate()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        DateTime now = new(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);

        teamAccount.ScheduleDeletion(now);

        teamAccount.Status.Should().Be(TeamAccountStatus.DeletionScheduled);
        teamAccount.ScheduledHardDeleteAt.Should().Be(now.AddDays(TeamAccount.DeletionGracePeriodDays));
    }

    [Fact]
    public void CancelScheduledDeletion_WhenScheduled_RestoresActive()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        teamAccount.ScheduleDeletion(DateTime.UtcNow);

        teamAccount.CancelScheduledDeletion();

        teamAccount.Status.Should().Be(TeamAccountStatus.Active);
        teamAccount.ScheduledHardDeleteAt.Should().BeNull();
    }

    [Fact]
    public void MarkProvisioningFailed_WhenAlreadyFailed_IsIdempotent()
    {
        TeamAccount teamAccount = TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        teamAccount.BeginProvisioning();
        teamAccount.MarkProvisioningFailed();

        teamAccount.MarkProvisioningFailed();

        teamAccount.Status.Should().Be(TeamAccountStatus.ProvisioningFailed);
    }

    [Theory]
    [InlineData(TeamAccountStatus.Active, true)]
    [InlineData(TeamAccountStatus.DeletionScheduled, true)]
    [InlineData(TeamAccountStatus.PendingVerification, false)]
    [InlineData(TeamAccountStatus.Provisioning, false)]
    [InlineData(TeamAccountStatus.ProvisioningFailed, false)]
    [InlineData(TeamAccountStatus.Deleted, false)]
    [InlineData(TeamAccountStatus.Archived, false)]
    public void AllowsTenantDataAccess_WhenStatusVaries_MatchesPolicy(
        TeamAccountStatus status,
        bool expected)
    {
        TeamAccount teamAccount = status == TeamAccountStatus.PendingVerification
            ? TeamAccount.RegisterForContactVerification(
                "Acme Corp",
                ValidSlug,
                ValidEmail,
                WellKnownSubscriptionPlans.FreeId,
                WellKnownLegalDocuments.TermsVersion,
                WellKnownLegalDocuments.PrivacyVersion)
            : TeamAccount.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        if (status != TeamAccountStatus.PendingVerification)
            teamAccount.BeginProvisioning();

        switch (status)
        {
            case TeamAccountStatus.PendingVerification:
                break;
            case TeamAccountStatus.Active:
                teamAccount.CompleteProvisioning();
                break;
            case TeamAccountStatus.DeletionScheduled:
                teamAccount.CompleteProvisioning();
                teamAccount.ScheduleDeletion(DateTime.UtcNow);
                break;
            case TeamAccountStatus.Provisioning:
                break;
            case TeamAccountStatus.ProvisioningFailed:
                teamAccount.MarkProvisioningFailed();
                break;
            case TeamAccountStatus.Deleted:
                teamAccount.CompleteProvisioning();
                teamAccount.MarkDeleted();
                break;
            case TeamAccountStatus.Archived:
                teamAccount.CompleteProvisioning();
                teamAccount.Archive();
                break;
        }

        teamAccount.AllowsTenantDataAccess().Should().Be(expected);
    }
}
