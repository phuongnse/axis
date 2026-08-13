using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Services;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

[Collection("IdentityDb")]
public sealed class WorkspaceMembershipRepositoryTests(IdentityDatabaseFixture database)
{
    [Fact]
    public async Task ProductBuilderAuthorization_WhenDependencyFails_ReturnsUnavailable()
    {
        IdentityDbContext context = database.CreateContext();
        WorkspaceProductBuilderAuthorization authorization = new(context);
        await context.DisposeAsync();

        WorkspaceProductBuilderDecision result = await authorization.AuthorizeAsync(
            Guid.NewGuid(),
            SubjectReference.Human(Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        result.Should().Be(WorkspaceProductBuilderDecision.Unavailable);
    }

    [Fact]
    public async Task ProductBuilderAuthorization_WhenMembershipIsEligible_AllowsOnlyActiveExplicitHumanMembership()
    {
        User creator = User.Create(
            "Product Builder",
            Email.Create($"builder-{Guid.NewGuid():N}@example.com").Value);
        Organization organization = Organization.Create("Builder Organization");
        Workspace workspace = Workspace.CreateOrganization(
            "Builder Organization",
            WorkspaceSlug.Create($"builder-organization-{Guid.NewGuid():N}").Value,
            organization.Id);
        WorkspaceMembership membership = WorkspaceMembership.CreateOrganizationCreator(
            workspace.Id,
            creator.Id);

        await using IdentityDbContext context = database.CreateContext();
        context.Users.Add(creator);
        context.Organizations.Add(organization);
        context.Workspaces.Add(workspace);
        context.WorkspaceMemberships.Add(membership);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        WorkspaceProductBuilderAuthorization authorization = new(context);

        (await authorization.AuthorizeAsync(
            workspace.Id,
            SubjectReference.Human(creator.Id),
            TestContext.Current.CancellationToken)).Should().Be(WorkspaceProductBuilderDecision.Allowed);
        (await authorization.AuthorizeAsync(
            workspace.Id,
            SubjectReference.Service(creator.Id),
            TestContext.Current.CancellationToken)).Should().Be(WorkspaceProductBuilderDecision.Denied);

        membership.Suspend(membership.Revision);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await authorization.AuthorizeAsync(
            workspace.Id,
            SubjectReference.Human(creator.Id),
            TestContext.Current.CancellationToken)).Should().Be(WorkspaceProductBuilderDecision.Denied);
    }

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
