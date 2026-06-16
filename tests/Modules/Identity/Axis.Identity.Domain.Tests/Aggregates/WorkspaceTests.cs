using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class WorkspaceTests
{
    private static WorkspaceSlug ValidSlug => WorkspaceSlug.Create("acme-corp").Value;
    private static Email ValidEmail => Email.Create("admin@acme.com").Value;

    [Fact]
    public void Workspace_WhenCreated_ProducesValidWorkspace()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Workspace.Name.Should().Be("Acme Corp");
        Workspace.Slug.Should().Be(ValidSlug);
        Workspace.OwnerEmail.Should().Be(ValidEmail);
        Workspace.Status.Should().Be(WorkspaceStatus.Active);
    }

    [Fact]
    public void Workspace_WhenCreated_RaisesWorkspaceCreatedEvent()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Workspace.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<WorkspaceCreated>();
    }

    [Fact]
    public void Workspace_WhenRegisteredForContactVerification_IsPendingAndRecordsLegalVersions()
    {
        Workspace Workspace = Workspace.RegisterTeamForContactVerification(
            "Acme Corp",
            ValidSlug,
            ValidEmail,
            WellKnownSubscriptionPlans.FreeId,
            WellKnownLegalDocuments.TermsVersion,
            WellKnownLegalDocuments.PrivacyVersion);

        Workspace.Status.Should().Be(WorkspaceStatus.PendingVerification);
        Workspace.AcceptedTermsVersion.Should().Be(WellKnownLegalDocuments.TermsVersion);
        Workspace.AcceptedPrivacyVersion.Should().Be(WellKnownLegalDocuments.PrivacyVersion);
        Workspace.LegalAcceptedAt.Should().NotBeNull();
    }

    [Fact]
    public void Workspace_WhenCreated_WorkspaceCreatedEventContainsCorrectData()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        WorkspaceCreated evt = Workspace.DomainEvents.OfType<WorkspaceCreated>().Single();
        evt.workspaceId.Should().Be(Workspace.Id);
        evt.Name.Should().Be("Acme Corp");
        evt.Slug.Should().Be("acme-corp");
        evt.OwnerEmail.Should().Be("admin@acme.com");
    }

    [Fact]
    public void Workspace_WhenArchived_ChangesStatusToArchived()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Workspace.ClearDomainEvents();

        Workspace.Archive();

        Workspace.Status.Should().Be(WorkspaceStatus.Archived);
    }

    [Fact]
    public void Workspace_WhenAlreadyArchived_ArchiveThrows()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Workspace.Archive();

        Action act = () => Workspace.Archive();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already archived*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Workspace_WhenCreatedWithEmptyName_Throws(string name)
    {
        Action act = () => Workspace.Create(name, ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BeginProvisioning_WhenProvisioningFailed_AllowsRetry()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Workspace.BeginProvisioning();
        Workspace.MarkProvisioningFailed();

        Workspace.BeginProvisioning();

        Workspace.Status.Should().Be(WorkspaceStatus.Provisioning);
    }

    [Fact]
    public void BeginProvisioningAfterContactVerification_WhenAlreadyProvisioning_DoesNotRaiseDuplicateEvent()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Workspace.BeginProvisioning();
        Workspace.ClearDomainEvents();

        Workspace.BeginProvisioningAfterContactVerification();

        Workspace.Status.Should().Be(WorkspaceStatus.Provisioning);
        Workspace.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MarkProvisioningFailed_WhenNotProvisioning_Throws()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Action act = () => Workspace.MarkProvisioningFailed();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only provisioning Workspaces*");
    }

    [Fact]
    public void UpdateProfile_WhenValid_UpdatesNameTimezoneAndLanguage()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Workspace.UpdateProfile("New Name", "Europe/Berlin", "de-DE");

        Workspace.Name.Should().Be("New Name");
        Workspace.TimeZoneId.Should().Be("Europe/Berlin");
        Workspace.DefaultLanguage.Should().Be("de-DE");
        Workspace.Slug.Should().Be(ValidSlug);
    }

    [Fact]
    public void UpdateProfile_WhenNameTooShort_Throws()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        Action act = () => Workspace.UpdateProfile("A", null, null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateLogoUrl_WhenDeleted_Throws()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Workspace.MarkDeleted();

        Action act = () => Workspace.UpdateLogoUrl("https://example.com/logo.png");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ScheduleDeletion_WhenActive_SetsDeletionScheduledAndGraceDate()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        DateTime now = new(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);

        Workspace.ScheduleDeletion(now);

        Workspace.Status.Should().Be(WorkspaceStatus.DeletionScheduled);
        Workspace.ScheduledHardDeleteAt.Should().Be(now.AddDays(Workspace.DeletionGracePeriodDays));
    }

    [Fact]
    public void CancelScheduledDeletion_WhenScheduled_RestoresActive()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Workspace.ScheduleDeletion(DateTime.UtcNow);

        Workspace.CancelScheduledDeletion();

        Workspace.Status.Should().Be(WorkspaceStatus.Active);
        Workspace.ScheduledHardDeleteAt.Should().BeNull();
    }

    [Fact]
    public void MarkProvisioningFailed_WhenAlreadyFailed_IsIdempotent()
    {
        Workspace Workspace = Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);
        Workspace.BeginProvisioning();
        Workspace.MarkProvisioningFailed();

        Workspace.MarkProvisioningFailed();

        Workspace.Status.Should().Be(WorkspaceStatus.ProvisioningFailed);
    }

    [Theory]
    [InlineData(WorkspaceStatus.Active, true)]
    [InlineData(WorkspaceStatus.DeletionScheduled, true)]
    [InlineData(WorkspaceStatus.PendingVerification, false)]
    [InlineData(WorkspaceStatus.Provisioning, false)]
    [InlineData(WorkspaceStatus.ProvisioningFailed, false)]
    [InlineData(WorkspaceStatus.Deleted, false)]
    [InlineData(WorkspaceStatus.Archived, false)]
    public void AllowsWorkspaceDataAccess_WhenStatusVaries_MatchesPolicy(
        WorkspaceStatus status,
        bool expected)
    {
        Workspace Workspace = status == WorkspaceStatus.PendingVerification
            ? Workspace.RegisterTeamForContactVerification(
                "Acme Corp",
                ValidSlug,
                ValidEmail,
                WellKnownSubscriptionPlans.FreeId,
                WellKnownLegalDocuments.TermsVersion,
                WellKnownLegalDocuments.PrivacyVersion)
            : Workspace.Create("Acme Corp", ValidSlug, ValidEmail, WellKnownSubscriptionPlans.FreeId);

        if (status != WorkspaceStatus.PendingVerification)
            Workspace.BeginProvisioning();

        switch (status)
        {
            case WorkspaceStatus.PendingVerification:
                break;
            case WorkspaceStatus.Active:
                Workspace.CompleteProvisioning();
                break;
            case WorkspaceStatus.DeletionScheduled:
                Workspace.CompleteProvisioning();
                Workspace.ScheduleDeletion(DateTime.UtcNow);
                break;
            case WorkspaceStatus.Provisioning:
                break;
            case WorkspaceStatus.ProvisioningFailed:
                Workspace.MarkProvisioningFailed();
                break;
            case WorkspaceStatus.Deleted:
                Workspace.CompleteProvisioning();
                Workspace.MarkDeleted();
                break;
            case WorkspaceStatus.Archived:
                Workspace.CompleteProvisioning();
                Workspace.Archive();
                break;
        }

        Workspace.AllowsWorkspaceDataAccess().Should().Be(expected);
    }
}
