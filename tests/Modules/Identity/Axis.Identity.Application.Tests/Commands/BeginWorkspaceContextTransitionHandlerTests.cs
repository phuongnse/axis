using Axis.Audit.Contracts;
using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class BeginWorkspaceContextTransitionHandlerTests
{
    [Fact]
    public async Task Begin_WhenSourceMembershipIsStale_AllowsRecoveryToActiveTarget()
    {
        Guid userId = Guid.NewGuid();
        Guid staleSourceWorkspaceId = Guid.NewGuid();
        Guid activeTargetWorkspaceId = Guid.NewGuid();
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        IWorkspaceContextTransitionRepository transitions = Substitute.For<IWorkspaceContextTransitionRepository>();
        IIdentityAuditOutbox audit = Substitute.For<IIdentityAuditOutbox>();
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        WorkspaceContextTransition? persistedTransition = null;
        AuditEventV1? persistedAudit = null;
        memberships.HasActiveWorkspaceAccessAsync(
                activeTargetWorkspaceId,
                userId,
                Arg.Any<CancellationToken>())
            .Returns(true);
        transitions.AddAsync(
                Arg.Any<WorkspaceContextTransition>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                persistedTransition = call.Arg<WorkspaceContextTransition>();
                return Task.CompletedTask;
            });
        transitions.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => persistedTransition);
        audit.EnqueueAsync(Arg.Any<AuditEventV1>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                persistedAudit = call.Arg<AuditEventV1>();
                return Task.CompletedTask;
            });
        audit.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => persistedAudit is null
                ? null
                : new IdentityAuditOutboxEntry(
                    persistedAudit,
                    IdentityAuditOutboxState.Pending));

        Result<WorkspaceContextTransitionDto> result = await new BeginWorkspaceContextTransitionHandler(
            memberships,
            transitions,
            audit,
            uow,
            TimeProvider.System,
            new WorkspaceContextTransitionPolicy(TimeSpan.FromMinutes(5), TimeSpan.FromHours(1)))
            .Handle(
                new BeginWorkspaceContextTransitionCommand(
                    userId,
                    staleSourceWorkspaceId,
                    activeTargetWorkspaceId,
                    new string('a', 64),
                    new string('b', 64),
                    "correlation"),
                TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.SourceWorkspaceId.Should().Be(staleSourceWorkspaceId);
        result.Value.TargetWorkspaceId.Should().Be(activeTargetWorkspaceId);
        await memberships.Received(1).HasActiveWorkspaceAccessAsync(
            activeTargetWorkspaceId,
            userId,
            Arg.Any<CancellationToken>());
        await memberships.DidNotReceive().HasActiveWorkspaceAccessAsync(
            staleSourceWorkspaceId,
            userId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Begin_WhenTargetMembershipIsUnavailable_DoesNotPersistOrDiscloseTarget()
    {
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        IWorkspaceContextTransitionRepository transitions = Substitute.For<IWorkspaceContextTransitionRepository>();
        IIdentityAuditOutbox audit = Substitute.For<IIdentityAuditOutbox>();
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();

        Result<WorkspaceContextTransitionDto> result = await new BeginWorkspaceContextTransitionHandler(
            memberships,
            transitions,
            audit,
            uow,
            TimeProvider.System,
            new WorkspaceContextTransitionPolicy(TimeSpan.FromMinutes(5), TimeSpan.FromHours(1)))
            .Handle(
                new BeginWorkspaceContextTransitionCommand(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    new string('a', 64),
                    new string('b', 64),
                    "correlation"),
                TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Be("Workspace context is unavailable.");
        await transitions.DidNotReceive().AddAsync(
            Arg.Any<WorkspaceContextTransition>(),
            Arg.Any<CancellationToken>());
        await audit.DidNotReceive().EnqueueAsync(
            Arg.Any<AuditEventV1>(),
            Arg.Any<CancellationToken>());
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
