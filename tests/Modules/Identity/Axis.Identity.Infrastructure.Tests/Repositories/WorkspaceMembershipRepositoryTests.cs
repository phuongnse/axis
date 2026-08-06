using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

[Collection("IdentityDb")]
public sealed class WorkspaceMembershipRepositoryTests(IdentityDatabaseFixture database)
{
    [Fact]
    public async Task HasActivePersonalOwnerWorkspaceAsync_WhenOnlyOrganizationAccessExists_ReturnsFalseUntilPersonalOwnerIsActive()
    {
        User user = User.Create(
            "Eligibility User",
            Email.Create($"eligibility-{Guid.NewGuid():N}@example.com").Value);
        user.VerifyEmail();
        Organization organization = Organization.Create("Eligibility Organization");
        Workspace organizationWorkspace = Workspace.CreateOrganization(
            "Eligibility Organization",
            WorkspaceSlug.Create($"eligibility-organization-{Guid.NewGuid():N}").Value,
            organization.Id);
        WorkspaceMembership organizationMembership = WorkspaceMembership.CreateOrganizationMember(
            organizationWorkspace.Id,
            user.Id,
            WorkspaceMembershipRole.Administrator);

        await using IdentityDbContext context = database.CreateContext();
        context.Users.Add(user);
        context.Organizations.Add(organization);
        context.Workspaces.Add(organizationWorkspace);
        context.WorkspaceMemberships.Add(organizationMembership);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        WorkspaceMembershipRepository repository = new(context);

        (await repository.HasActivePersonalOwnerWorkspaceAsync(
            user.Id,
            TestContext.Current.CancellationToken)).Should().BeFalse();

        Workspace personalWorkspace = Workspace.CreatePersonal(
            "Eligibility Personal",
            WorkspaceSlug.Create($"eligibility-personal-{Guid.NewGuid():N}").Value);
        personalWorkspace.ActivateAfterOwnerVerification();
        context.Workspaces.Add(personalWorkspace);
        context.WorkspaceMemberships.Add(
            WorkspaceMembership.CreatePersonalOwner(personalWorkspace.Id, user.Id));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await repository.HasActivePersonalOwnerWorkspaceAsync(
            user.Id,
            TestContext.Current.CancellationToken)).Should().BeTrue();
    }
}
