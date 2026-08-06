using Axis.Audit.Contracts;
using Axis.Identity.Application.Commands.CompleteWorkspaceContextTransition;
using Axis.Identity.Application.Commands.CreateOrganizationWorkspace;
using Axis.Identity.Application.Commands.SignInUser;
using Axis.Identity.Application.Commands.ValidateWorkspaceAccess;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests;

public sealed class GovernedWorkspaceMembershipTests
{
    [Fact]
    public async Task CreateOrganizationWorkspace_WhenHandled_CreatesOwnerAndAdministratorMemberships()
    {
        IUserRepository users = Substitute.For<IUserRepository>();
        IOrganizationRepository organizations = Substitute.For<IOrganizationRepository>();
        IOrganizationMembershipRepository organizationMemberships = Substitute.For<IOrganizationMembershipRepository>();
        IWorkspaceRepository workspaces = Substitute.For<IWorkspaceRepository>();
        IWorkspaceMembershipRepository workspaceMemberships = Substitute.For<IWorkspaceMembershipRepository>();
        ICreateOrganizationIdempotencyRepository idempotency = Substitute.For<ICreateOrganizationIdempotencyRepository>();
        IWorkspaceSlugGenerator slugs = Substitute.For<IWorkspaceSlugGenerator>();
        IIdentityAuditOutbox audit = Substitute.For<IIdentityAuditOutbox>();
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        User user = User.Create("Ada", Email.Create("ada@example.com").Value); user.VerifyEmail();
        users.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        slugs.GenerateUniqueSlugAsync("Acme", Arg.Any<CancellationToken>()).Returns(WorkspaceSlug.Create("acme").Value);
        Organization? createdOrganization = null;
        Workspace? createdWorkspace = null;
        organizations.AddAsync(Arg.Any<Organization>(), Arg.Any<CancellationToken>()).Returns(call => { createdOrganization = call.Arg<Organization>(); return Task.CompletedTask; });
        workspaces.AddAsync(Arg.Any<Workspace>(), Arg.Any<CancellationToken>()).Returns(call => { createdWorkspace = call.Arg<Workspace>(); return Task.CompletedTask; });
        organizations.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => createdOrganization?.Id == call.Arg<Guid>() ? createdOrganization : null);
        workspaces.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => createdWorkspace?.Id == call.Arg<Guid>() ? createdWorkspace : null);

        Result<CreateOrganizationWorkspaceDto> result = await new CreateOrganizationWorkspaceHandler(users, organizations, organizationMemberships, workspaces, workspaceMemberships, idempotency, slugs, audit, uow).Handle(new CreateOrganizationWorkspaceCommand(user.Id, " Acme ", "key", "correlation"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await organizationMemberships.Received(1).AddAsync(Arg.Is<OrganizationMembership>(x => x.UserId == user.Id && x.Role == OrganizationMembershipRole.Owner), Arg.Any<CancellationToken>());
        await workspaceMemberships.Received(1).AddAsync(Arg.Is<WorkspaceMembership>(x => x.UserId == user.Id && x.Role == WorkspaceMembershipRole.Administrator), Arg.Any<CancellationToken>());
        await audit.Received(1).EnqueueAsync(
            Arg.Is<AuditEventV1>(entry =>
                entry.ActorId == user.Id
                && entry.SubjectId == user.Id
                && entry.WorkspaceId != Guid.Empty
                && entry.TargetType == "Organization"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignIn_WhenPersonalOwnerIsActive_UsesMembership()
    {
        IUserRepository users = Substitute.For<IUserRepository>();
        IWorkspaceRepository workspaces = Substitute.For<IWorkspaceRepository>();
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        IPasswordHasher passwords = Substitute.For<IPasswordHasher>();
        User user = User.Create("Ada", Email.Create("ada@example.com").Value);
        user.SetPasswordHash("hash"); user.VerifyEmail();
        Workspace workspace = Workspace.CreatePersonal("Ada Workspace", WorkspaceSlug.Create("ada").Value);
        workspace.ActivateAfterOwnerVerification();
        users.FindByEmailGloballyAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        passwords.Verify("password", "hash").Returns(true);
        memberships.ListActiveForUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(new[] { WorkspaceMembership.CreatePersonalOwner(workspace.Id, user.Id) });
        workspaces.GetByIdAsync(workspace.Id, Arg.Any<CancellationToken>()).Returns(workspace);

        Result<SignInSuccessDto> result = await new SignInUserHandler(users, workspaces, memberships, passwords).Handle(new SignInUserCommand("ada@example.com", "password"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.workspaceId.Should().Be(workspace.Id);
    }

    [Fact]
    public async Task ValidateAccess_WhenOnlyOrganizationMembershipExists_DeniesAccess()
    {
        IWorkspaceRepository workspaces = Substitute.For<IWorkspaceRepository>();
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        Guid userId = Guid.NewGuid();
        Workspace workspace = Workspace.CreateOrganization("Acme", WorkspaceSlug.Create("acme").Value, Guid.NewGuid());
        workspaces.GetByIdAsync(workspace.Id, Arg.Any<CancellationToken>()).Returns(workspace);

        Result<WorkspaceAccessDto> result = await new ValidateWorkspaceAccessHandler(workspaces, memberships).Handle(new ValidateWorkspaceAccessCommand(userId, workspace.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }
}
