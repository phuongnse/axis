using Axis.Audit.Contracts;
using Axis.Authorization.Application;
using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Shared.Domain.Primitives;
using NSubstitute;

namespace Axis.Authorization.Application.Tests;

public sealed class ProductRoleAssignmentServiceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-07T00:00:00Z");

    [Fact]
    public async Task Assign_WhenValid_CommitsCanonicalAssignmentAndAudit()
    {
        TestContextData context = Context();
        ConfigureAuditReadBack(context.Audit);

        ProductRoleAssignmentResult result = await context.Service.AssignAsync(
            context.AssignRequest,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Assignment!.IsActive);
        Assert.Equal(1, result.Assignment.Revision);
        await context.Store.Received(1).AddAsync(
            Arg.Any<StoredProductRoleAssignment>(),
            Arg.Any<CancellationToken>());
        await context.Store.Received(1).AddIdempotencyAsync(
            Arg.Any<ProductRoleIdempotencyRecord>(),
            Arg.Any<CancellationToken>());
        await context.UnitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Assign_WhenAuditReadBackFails_RollsBackWithoutSuccess()
    {
        TestContextData context = Context();
        context.Audit.ReadBackAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((AuditEventReadBackV1?)null);

        ProductRoleAssignmentResult result = await context.Service.AssignAsync(
            context.AssignRequest,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("audit_unavailable", result.Error);
        await context.UnitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await context.UnitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Assign_WhenEquivalentRetry_ReturnsCanonicalWithoutMutation()
    {
        TestContextData context = Context();
        ConfigureAuditReadBack(context.Audit);
        ProductRoleAssignmentResult first = await context.Service.AssignAsync(
            context.AssignRequest,
            TestContext.Current.CancellationToken);
        StoredProductRoleAssignment stored = new(
            Guid.NewGuid(),
            context.AssignRequest.WorkspaceId,
            context.AssignRequest.Target,
            context.AssignRequest.PolicyVersionId,
            context.AssignRequest.RoleKey,
            true,
            1,
            Now,
            null,
            Now,
            ActorSnapshot.User(context.AssignRequest.Actor.Id, context.AssignRequest.ActorDisplayName),
            ActorSnapshot.User(context.AssignRequest.Actor.Id, context.AssignRequest.ActorDisplayName));
        ProductRoleIdempotencyRecord captured = context.Store.ReceivedCalls()
            .Select(call => call.GetArguments().FirstOrDefault())
            .OfType<ProductRoleIdempotencyRecord>()
            .Single();
        context.Store.GetIdempotencyAsync(
                context.AssignRequest.WorkspaceId,
                context.AssignRequest.IdempotencyKey,
                Arg.Any<CancellationToken>())
            .Returns(captured);
        context.Store.GetByIdAsync(captured.AssignmentId, Arg.Any<CancellationToken>())
            .Returns(stored);
        context.Audit.ReadBackAsync(captured.AuditEventId, Arg.Any<CancellationToken>())
            .Returns(ReadBack(new AuditEventV1(
                captured.AuditEventId,
                AuditActorKindV1.Human,
                context.AssignRequest.Actor.Id,
                context.AssignRequest.Target.Id,
                context.AssignRequest.WorkspaceId,
                "authorization.assignment",
                "product-role",
                context.AssignRequest.PolicyVersionId,
                "assigned",
                Now,
                context.AssignRequest.CorrelationId,
                new Dictionary<string, string> { ["role"] = context.AssignRequest.RoleKey })));

        ProductRoleAssignmentResult retry = await context.Service.AssignAsync(
            context.AssignRequest,
            TestContext.Current.CancellationToken);

        Assert.True(first.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.Equal(stored, retry.Assignment);
        await context.Store.Received(1).AddAsync(
            Arg.Any<StoredProductRoleAssignment>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revoke_WhenRevisionMatches_CommitsInactiveAssignment()
    {
        TestContextData context = Context();
        ConfigureAuditReadBack(context.Audit);
        StoredProductRoleAssignment current = new(
            Guid.NewGuid(),
            context.AssignRequest.WorkspaceId,
            context.AssignRequest.Target,
            context.AssignRequest.PolicyVersionId,
            context.AssignRequest.RoleKey,
            true,
            4,
            Now.AddDays(-1),
            null,
            Now.AddDays(-1),
            ActorSnapshot.User(context.AssignRequest.Actor.Id, context.AssignRequest.ActorDisplayName),
            ActorSnapshot.User(context.AssignRequest.Actor.Id, context.AssignRequest.ActorDisplayName));
        context.Store.GetAsync(
                current.WorkspaceId,
                current.Subject,
                current.PolicyVersionId,
                current.RoleKey,
                Arg.Any<CancellationToken>())
            .Returns(current);
        RevokeProductRoleRequest request = new(
            current.WorkspaceId,
            context.AssignRequest.Actor,
            current.Subject,
            current.PolicyVersionId,
            current.RoleKey,
            "revoke-1",
            "corr-revoke-1",
            "Axis Admin",
            4);

        ProductRoleAssignmentResult result = await context.Service.RevokeAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.Assignment!.IsActive);
        Assert.Equal(5, result.Assignment.Revision);
        await context.Store.Received(1).SaveAsync(
            Arg.Is<StoredProductRoleAssignment>(value => !value.IsActive && value.Revision == 5),
            4,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Assign_WhenActorUnauthorized_AuditsDenialWithoutMutation()
    {
        TestContextData context = Context(administrator: false);
        ConfigureAuditReadBack(context.Audit);

        ProductRoleAssignmentResult result = await context.Service.AssignAsync(
            context.AssignRequest,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("authority_denied", result.Error);
        await context.Store.DidNotReceive().AddAsync(
            Arg.Any<StoredProductRoleAssignment>(),
            Arg.Any<CancellationToken>());
        await context.UnitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Assign_WhenTargetIsMalformed_AuditsAuthenticatedSafeDenial()
    {
        TestContextData context = Context();
        ConfigureAuditReadBack(context.Audit);

        ProductRoleAssignmentResult result = await context.Service.AssignAsync(
            context.AssignRequest with { Target = default },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid", result.Error);
        AuditEventV1 auditEvent = Assert.Single(
            context.Audit.ReceivedCalls()
                .Select(call => call.GetArguments().FirstOrDefault())
                .OfType<AuditEventV1>());
        Assert.Equal(AuditActorKindV1.Human, auditEvent.ActorKind);
        Assert.Equal(context.AssignRequest.Actor.Id, auditEvent.ActorId);
        Assert.Equal(context.AssignRequest.Actor.Id, auditEvent.SubjectId);
        Assert.Equal(context.AssignRequest.WorkspaceId, auditEvent.WorkspaceId);
        Assert.Equal(context.AssignRequest.CorrelationId, auditEvent.CorrelationId);
        Assert.Equal("invalid_request", auditEvent.Outcome);
        Assert.Equal("invalid", auditEvent.Metadata?["request"]);
        Assert.True(AuditEventV1Validator.Validate(auditEvent).IsValid);
    }

    [Fact]
    public async Task Assign_WhenCallerIdentityIsMalformed_AuditsAnonymousSafeEnvelope()
    {
        TestContextData context = Context();
        ConfigureAuditReadBack(context.Audit);

        ProductRoleAssignmentResult result = await context.Service.AssignAsync(
            context.AssignRequest with
            {
                WorkspaceId = Guid.Empty,
                Actor = default,
                Target = default,
                RoleKey = string.Empty,
                IdempotencyKey = string.Empty,
                CorrelationId = new string('x', 121),
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid", result.Error);
        AuditEventV1 auditEvent = Assert.Single(
            context.Audit.ReceivedCalls()
                .Select(call => call.GetArguments().FirstOrDefault())
                .OfType<AuditEventV1>());
        Assert.Equal(AuditActorKindV1.Anonymous, auditEvent.ActorKind);
        Assert.Null(auditEvent.ActorId);
        Assert.Null(auditEvent.SubjectId);
        Assert.Null(auditEvent.WorkspaceId);
        Assert.StartsWith("authorization-", auditEvent.CorrelationId, StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, auditEvent.TargetId);
        Assert.True(AuditEventV1Validator.Validate(auditEvent).IsValid);
    }

    private static TestContextData Context(bool administrator = true)
    {
        IAuthorizationSubjectActivity activity = Substitute.For<IAuthorizationSubjectActivity>();
        IAuthorizationAdministratorAuthority administrators =
            Substitute.For<IAuthorizationAdministratorAuthority>();
        IInstalledProductRoleStore roles = Substitute.For<IInstalledProductRoleStore>();
        IProductRoleAssignmentStore store = Substitute.For<IProductRoleAssignmentStore>();
        IAuthorizationAuditSink audit = Substitute.For<IAuthorizationAuditSink>();
        IAuthorizationUnitOfWork unitOfWork = Substitute.For<IAuthorizationUnitOfWork>();
        Guid workspaceId = Guid.NewGuid();
        SubjectReference actor = SubjectReference.Human(Guid.NewGuid());
        SubjectReference target = SubjectReference.Service(Guid.NewGuid());
        Guid policyVersionId = Guid.NewGuid();
        AssignProductRoleRequest request = new(
            workspaceId,
            actor,
            target,
            policyVersionId,
            "Applicant",
            "request-1",
            "corr-request-1",
            "Axis Admin");
        administrators.IsAdministratorAsync(workspaceId, actor, Arg.Any<CancellationToken>())
            .Returns(administrator);
        activity.IsActiveAsync(workspaceId, target, Arg.Any<CancellationToken>())
            .Returns(true);
        roles.ExistsAsync(workspaceId, policyVersionId, "Applicant", Arg.Any<CancellationToken>())
            .Returns(true);
        audit.IngestAsync(Arg.Any<AuditEventV1>(), Arg.Any<CancellationToken>())
            .Returns(new AuditIngestionResult(AuditIngestionDisposition.Stored, null));
        return new(
            new ProductRoleAssignmentService(
                activity,
                administrators,
                roles,
                store,
                audit,
                unitOfWork,
                new FixedClock(Now)),
            store,
            audit,
            unitOfWork,
            request);
    }

    private static void ConfigureAuditReadBack(IAuthorizationAuditSink audit)
    {
        AuditEventV1? captured = null;
        audit.IngestAsync(Arg.Any<AuditEventV1>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<AuditEventV1>();
                return new AuditIngestionResult(AuditIngestionDisposition.Stored, null);
            });
        audit.ReadBackAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => captured is null ? null : ReadBack(captured));
    }

    private static AuditEventReadBackV1 ReadBack(AuditEventV1 value) =>
        new(
            value.EventId,
            value.ActorKind,
            value.ActorId,
            value.SubjectId,
            value.WorkspaceId,
            value.Action,
            value.TargetType,
            value.TargetId,
            value.Outcome,
            value.OccurredAt,
            value.CorrelationId,
            value.Metadata ?? new Dictionary<string, string>());

    private sealed record TestContextData(
        ProductRoleAssignmentService Service,
        IProductRoleAssignmentStore Store,
        IAuthorizationAuditSink Audit,
        IAuthorizationUnitOfWork UnitOfWork,
        AssignProductRoleRequest AssignRequest);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
