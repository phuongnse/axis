using Axis.Audit.Contracts;
using Axis.Identity.Application.Commands.SetWorkspaceProductBuilder;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class SetWorkspaceProductBuilderHandlerTests
{
    [Fact]
    public async Task Grant_WhenAuthorized_PersistsExplicitStateAndRedactedAudit()
    {
        TestContext context = new();

        Result<WorkspaceProductBuilderDto> result = await context.Handle(enabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsProductBuilder.Should().BeTrue();
        result.Value.MembershipRevision.Should().Be(2);
        context.CapturedAudit.Should().NotBeNull();
        context.CapturedAudit!.SubjectId.Should().Be(context.Target.Id);
        context.CapturedAudit.Metadata.Should().NotContainKey("email");
        context.CapturedAudit.Metadata.Should().NotContainKey("displayName");
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetProductBuilder_WhenGrantIsEquivalent_ReturnsCanonicalStateWithoutIncrementingRevision()
    {
        TestContext context = new();
        DateTime originalModifiedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ActorSnapshot originalActor = ActorSnapshot.User(context.Target.Id, context.Target.FullName);
        context.TargetMembership.InitializeMetadata(originalActor, originalModifiedAt);
        context.TargetMembership.SetProductBuilder(true, context.TargetMembership.Revision);
        int revision = context.TargetMembership.Revision;

        Result<WorkspaceProductBuilderDto> result = await context.Handle(
            enabled: true,
            expectedRevision: revision);

        result.IsSuccess.Should().BeTrue();
        result.Value.MembershipRevision.Should().Be(revision);
        context.TargetMembership.Revision.Should().Be(revision);
        context.TargetMembership.UpdatedAt.Should().Be(originalModifiedAt);
        context.TargetMembership.UpdatedBy.Should().Be(originalActor);
    }

    [Fact]
    public async Task SetProductBuilder_WhenTargetIsSelf_IsDeniedAndAuditedWithoutTargetDisclosure()
    {
        TestContext context = new();

        Result<WorkspaceProductBuilderDto> result = await context.Handle(
            enabled: true,
            targetUserId: context.Actor.Id);

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        context.CapturedAudit!.Outcome.Should().Be("authority_denied");
        context.CapturedAudit.Metadata.Should().NotContainKey("targetUserId");
    }

    [Fact]
    public async Task SetProductBuilder_WhenTargetIsForeign_IsNotFoundAndAuditedWithoutForeignDisclosure()
    {
        TestContext context = new();

        Result<WorkspaceProductBuilderDto> result = await context.Handle(
            enabled: true,
            targetUserId: Guid.NewGuid());

        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        context.CapturedAudit!.Outcome.Should().Be("target_unavailable");
        context.CapturedAudit.SubjectId.Should().Be(context.Actor.Id);
    }

    [Fact]
    public async Task Denial_WhenAuditDependencyFails_FailsClosed()
    {
        TestContext context = new();
        context.Audit.EnqueueAsync(Arg.Any<AuditEventV1>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("audit unavailable")));

        Result<WorkspaceProductBuilderDto> result = await context.Handle(
            enabled: true,
            targetUserId: context.Actor.Id);

        result.IsFailure.Should().BeTrue();
        result.ProblemCode.Should().Be(IdentityProblemCodes.ProductBuilderAuditUnavailable);
    }

    [Fact]
    public async Task SetProductBuilder_WhenConcurrentMutationOccurs_ReturnsAuditedConflict()
    {
        TestContext context = new();
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<int>(new Axis.Shared.Application.ConcurrencyException()),
                _ => Task.FromResult(1));

        Result<WorkspaceProductBuilderDto> result = await context.Handle(enabled: true);

        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        context.CapturedAudit!.Outcome.Should().Be("concurrent_change");
        await context.UnitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private sealed class TestContext
    {
        private readonly IWorkspaceRepository _workspaces = Substitute.For<IWorkspaceRepository>();
        private readonly IWorkspaceMembershipRepository _memberships = Substitute.For<IWorkspaceMembershipRepository>();
        public IIdentityAuditOutbox Audit { get; } = Substitute.For<IIdentityAuditOutbox>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public User Actor { get; } = User.Create("Administrator", Email.Create("administrator@example.com").Value);
        public User Target { get; } = User.Create("Builder", Email.Create("builder@example.com").Value);
        public WorkspaceMembership TargetMembership => _targetMembership;
        public Workspace Workspace { get; }
        public AuditEventV1? CapturedAudit { get; private set; }
        private readonly WorkspaceMembership _actorMembership;
        private readonly WorkspaceMembership _targetMembership;

        public TestContext()
        {
            Organization organization = Organization.Create("Acme");
            Workspace = Workspace.CreateOrganization(
                "Acme",
                WorkspaceSlug.Create($"acme-{Guid.NewGuid():N}").Value,
                organization.Id);
            _actorMembership = WorkspaceMembership.CreateOrganizationMember(
                Workspace.Id,
                Actor.Id,
                WorkspaceMembershipRole.Administrator);
            _targetMembership = WorkspaceMembership.CreateOrganizationMember(
                Workspace.Id,
                Target.Id,
                WorkspaceMembershipRole.Member);
            _workspaces.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>()).Returns(Workspace);
            _memberships.GetActiveHumanAsync(Workspace.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<Guid>(1) switch
                {
                    var id when id == Actor.Id => _actorMembership,
                    var id when id == Target.Id => _targetMembership,
                    _ => null,
                });
            _memberships.ListActiveForWorkspaceAsync(Workspace.Id, Arg.Any<CancellationToken>())
                .Returns(_ => new[]
                {
                    new ActiveWorkspaceHumanProjection(
                        Target.Id,
                        Target.FullName,
                        Target.Email.Value,
                        _targetMembership.Role,
                        _targetMembership.IsProductBuilder,
                        _targetMembership.Revision),
                });
            Audit.EnqueueAsync(Arg.Any<AuditEventV1>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    CapturedAudit = call.Arg<AuditEventV1>();
                    return Task.CompletedTask;
                });
            Audit.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => CapturedAudit?.EventId == call.Arg<Guid>()
                    ? new IdentityAuditOutboxEntry(CapturedAudit, IdentityAuditOutboxState.Pending)
                    : null);
        }

        public Task<Result<WorkspaceProductBuilderDto>> Handle(
            bool enabled,
            Guid? targetUserId = null,
            int? expectedRevision = null) =>
            new SetWorkspaceProductBuilderHandler(
                _workspaces,
                _memberships,
                Audit,
                TimeProvider.System,
                UnitOfWork).Handle(
                    new SetWorkspaceProductBuilderCommand(
                        Actor.Id,
                        Workspace.Id,
                        targetUserId ?? Target.Id,
                        enabled,
                        expectedRevision ?? _targetMembership.Revision,
                        "correlation",
                        Actor.FullName),
                    CancellationToken.None);
    }
}
