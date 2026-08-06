using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public sealed class WorkspaceTests
{
    [Fact]
    public void CreatePersonal_WhenCreated_HasNoOrganizationAndAwaitsVerification()
    {
        Workspace workspace = Workspace.CreatePersonal("Ada Workspace", WorkspaceSlug.Create("ada").Value);
        workspace.Type.Should().Be(WorkspaceType.Personal);
        workspace.OrganizationId.Should().BeNull();
        workspace.Status.Should().Be(WorkspaceStatus.PendingVerification);
        workspace.Revision.Should().Be(1);
    }

    [Fact]
    public void CreateOrganization_WhenCreated_BindsExactlyOneOrganization()
    {
        Guid organizationId = Guid.NewGuid();
        Workspace workspace = Workspace.CreateOrganization("Acme", WorkspaceSlug.Create("acme").Value, organizationId);
        workspace.Type.Should().Be(WorkspaceType.Organization);
        workspace.OrganizationId.Should().Be(organizationId);
    }

    [Fact]
    public void SetStatus_WithStaleRevision_RejectsUpdate()
    {
        Workspace workspace = Workspace.CreatePersonal("Ada Workspace", WorkspaceSlug.Create("ada").Value);
        Action act = () => workspace.SetStatus(WorkspaceStatus.Active, 0);
        act.Should().Throw<InvalidOperationException>();
    }
}
