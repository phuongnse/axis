using Axis.Identity.Application.Queries.ListAssignableSubjects;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class ListAssignableSubjectsHandlerTests
{
    [Fact]
    public async Task ListAssignableSubjects_WhenAdministrator_ReturnsOnlyActiveWorkspaceSubjects()
    {
        Guid actorId = Guid.NewGuid();
        Guid workspaceId = Guid.NewGuid();
        Guid humanId = Guid.NewGuid();
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        memberships.GetActiveAsync(workspaceId, actorId, Arg.Any<CancellationToken>())
            .Returns(WorkspaceMembership.CreateOrganizationMember(
                workspaceId,
                actorId,
                WorkspaceMembershipRole.Administrator));
        memberships.ListActiveForWorkspaceAsync(workspaceId, Arg.Any<CancellationToken>())
            .Returns([new ActiveWorkspaceHumanProjection(humanId, "Active Human", "active@example.com")]);

        ServiceIdentity active = ServiceIdentity.Create(workspaceId, "service-active", DateTime.UtcNow);
        ServiceIdentity revoked = ServiceIdentity.Create(workspaceId, "service-revoked", DateTime.UtcNow);
        revoked.Revoke(revoked.Revision, DateTime.UtcNow);
        IServiceIdentityRepository identities = Substitute.For<IServiceIdentityRepository>();
        identities.ListAsync(workspaceId, Arg.Any<CancellationToken>())
            .Returns([active, revoked]);

        Result<IReadOnlyList<AssignableSubjectDto>> result = await new ListAssignableSubjectsHandler(
            memberships,
            identities).Handle(
                new ListAssignableSubjectsQuery(actorId, workspaceId),
                TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(value =>
            value.Subject.Kind == SubjectKind.Human &&
            value.Subject.SubjectId == humanId &&
            value.DisplayName == "Active Human" &&
            value.SecondaryLabel == "active@example.com");
        result.Value.Should().Contain(value =>
            value.Subject.Kind == SubjectKind.Service &&
            value.Subject.SubjectId == active.Id &&
            value.DisplayName == "service-active");
        result.Value.Should().NotContain(value => value.Subject.SubjectId == revoked.Id);
    }

    [Fact]
    public async Task ListAssignableSubjects_WhenAdministratorMissing_DeniesWithoutReadingSubjects()
    {
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        IServiceIdentityRepository identities = Substitute.For<IServiceIdentityRepository>();

        Result<IReadOnlyList<AssignableSubjectDto>> result = await new ListAssignableSubjectsHandler(
            memberships,
            identities).Handle(
                new ListAssignableSubjectsQuery(Guid.NewGuid(), Guid.NewGuid()),
                TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        await memberships.DidNotReceive().ListActiveForWorkspaceAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await identities.DidNotReceive().ListAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }
}
