using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class WorkspaceTests
{
    private static WorkspaceSlug ValidSlug => WorkspaceSlug.Create("alice-smith").Value;
    private static Email ValidEmail => Email.Create("alice@example.com").Value;

    [Fact]
    public void CreatePersonal_WhenCalled_CreatesPendingPersonalWorkspace()
    {
        Guid ownerUserId = Guid.NewGuid();

        Workspace workspace = Workspace.CreatePersonal(
            "Alice Smith",
            ValidSlug,
            ValidEmail,
            ownerUserId);

        workspace.Name.Should().Be("Alice Smith");
        workspace.Slug.Should().Be(ValidSlug);
        workspace.OwnerEmail.Should().Be(ValidEmail);
        workspace.OwnerUserId.Should().Be(ownerUserId);
        workspace.Type.Should().Be(WorkspaceType.Personal);
        workspace.Status.Should().Be(WorkspaceStatus.PendingVerification);
        workspace.AllowsSignIn().Should().BeFalse();
    }

    [Fact]
    public void ActivateAfterOwnerVerification_WhenPending_ActivatesWorkspace()
    {
        Workspace workspace = Workspace.CreatePersonal(
            "Alice Smith",
            ValidSlug,
            ValidEmail,
            Guid.NewGuid());

        workspace.ActivateAfterOwnerVerification();

        workspace.Status.Should().Be(WorkspaceStatus.Active);
        workspace.AllowsSignIn().Should().BeTrue();
    }

    [Fact]
    public void RecordLegalAcceptance_WhenCalled_StoresCurrentVersions()
    {
        Workspace workspace = Workspace.CreatePersonal(
            "Alice Smith",
            ValidSlug,
            ValidEmail,
            Guid.NewGuid());

        workspace.RecordLegalAcceptance(
            WellKnownLegalDocuments.TermsVersion,
            WellKnownLegalDocuments.PrivacyVersion);

        workspace.AcceptedTermsVersion.Should().Be(WellKnownLegalDocuments.TermsVersion);
        workspace.AcceptedPrivacyVersion.Should().Be(WellKnownLegalDocuments.PrivacyVersion);
        workspace.LegalAcceptedAt.Should().NotBeNull();
    }
}
