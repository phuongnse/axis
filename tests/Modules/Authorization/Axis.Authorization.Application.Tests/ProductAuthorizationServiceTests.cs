using Axis.Audit.Contracts;
using Axis.Authorization.Application;
using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using NSubstitute;

namespace Axis.Authorization.Application.Tests;

public sealed class ProductAuthorizationServiceTests
{
    [Fact]
    public async Task Authorize_WhenExactRecordGrantExists_ReturnsOwn()
    {
        TestContextData context = Context(
            [new("Applicant", "record.read", "record", null, ProductActionScope.Own)]);

        ProductAuthorizationDecision decision = await context.Service.AuthorizeAsync(
            context.Request,
            TestContext.Current.CancellationToken);

        Assert.True(decision.IsAllowed);
        Assert.Equal(ProductActionScope.Own, decision.Scope);
        await context.UnitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WhenMultipleRolesMatch_AllDominatesOwn()
    {
        TestContextData context = Context(
        [
            new("Applicant", "record.read", "record", null, ProductActionScope.Own),
            new("Caseworker", "record.read", "record", null, ProductActionScope.All),
        ]);

        ProductAuthorizationDecision decision = await context.Service.AuthorizeAsync(
            context.Request,
            TestContext.Current.CancellationToken);

        Assert.True(decision.IsAllowed);
        Assert.Equal(ProductActionScope.All, decision.Scope);
    }

    [Fact]
    public async Task Authorize_WhenResourceKeyDiffers_DoesNotTreatNullAsWildcard()
    {
        TestContextData context = Context(
            [new("Applicant", "record.read", "record", null, ProductActionScope.Own)]);
        ProductAuthorizationRequest keyed = context.Request with { ResourceKey = "definition-a" };

        ProductAuthorizationDecision decision = await context.Service.AuthorizeAsync(
            keyed,
            TestContext.Current.CancellationToken);

        Assert.False(decision.IsAllowed);
        Assert.Equal(ProductAuthorizationDecisionStatus.Denied, decision.Status);
    }

    [Fact]
    public async Task Authorize_WhenSubjectInactive_DeniesBeforePolicyRead()
    {
        TestContextData context = Context([], active: false);

        ProductAuthorizationDecision decision = await context.Service.AuthorizeAsync(
            context.Request,
            TestContext.Current.CancellationToken);

        Assert.False(decision.IsAllowed);
        await context.Store.DidNotReceiveWithAnyArgs().ListActiveGrantsAsync(
            default,
            default,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Authorize_WhenAuditReadBackFails_ReturnsUnavailable()
    {
        TestContextData context = Context(
            [new("Applicant", "record.read", "record", null, ProductActionScope.All)],
            auditReadBack: false);

        ProductAuthorizationDecision decision = await context.Service.AuthorizeAsync(
            context.Request,
            TestContext.Current.CancellationToken);

        Assert.False(decision.IsAllowed);
        Assert.True(decision.IsUnavailable);
        await context.UnitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await context.UnitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WhenRequestShapeIsInvalid_AuditsAuthenticatedDenial()
    {
        TestContextData context = Context([]);

        ProductAuthorizationDecision decision = await context.Service.AuthorizeAsync(
            context.Request with { ActionKey = string.Empty },
            TestContext.Current.CancellationToken);

        Assert.False(decision.IsAllowed);
        Assert.Equal(ProductAuthorizationDecisionStatus.Denied, decision.Status);
        AuditEventV1 auditEvent = Assert.Single(
            context.Audit.ReceivedCalls()
                .Select(call => call.GetArguments().FirstOrDefault())
                .OfType<AuditEventV1>());
        Assert.Equal(AuditActorKindV1.Human, auditEvent.ActorKind);
        Assert.Equal(context.Request.Subject.Id, auditEvent.ActorId);
        Assert.Equal(context.Request.Subject.Id, auditEvent.SubjectId);
        Assert.Equal(context.Request.WorkspaceId, auditEvent.WorkspaceId);
        Assert.Equal(context.Request.CorrelationId, auditEvent.CorrelationId);
        Assert.Equal("invalid_request", auditEvent.Outcome);
        Assert.Equal("invalid", auditEvent.Metadata?["request"]);
        Assert.True(AuditEventV1Validator.Validate(auditEvent).IsValid);
        await context.UnitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WhenCallerIdentityIsInvalid_AuditsAnonymousSafeEnvelope()
    {
        TestContextData context = Context([]);

        ProductAuthorizationDecision decision = await context.Service.AuthorizeAsync(
            context.Request with
            {
                WorkspaceId = Guid.Empty,
                Subject = default,
                ActionKey = string.Empty,
                CorrelationId = new string('x', 121),
            },
            TestContext.Current.CancellationToken);

        Assert.False(decision.IsAllowed);
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

    [Fact]
    public async Task Authorize_WhenDependencyFails_ReturnsUnavailableAndAuditsDistinctOutcome()
    {
        TestContextData context = Context([], dependencyFailure: true);

        ProductAuthorizationDecision decision = await context.Service.AuthorizeAsync(
            context.Request,
            TestContext.Current.CancellationToken);

        Assert.True(decision.IsUnavailable);
        AuditEventV1 auditEvent = Assert.Single(
            context.Audit.ReceivedCalls()
                .Select(call => call.GetArguments().FirstOrDefault())
                .OfType<AuditEventV1>());
        Assert.Equal("dependency_failure", auditEvent.Outcome);
        Assert.Equal("Unavailable", auditEvent.Metadata?["scope"]);
    }

    [Fact]
    public async Task Authorize_WhenInvalidRequestAuditFails_ReturnsUnavailable()
    {
        TestContextData context = Context([], auditReadBack: false);

        ProductAuthorizationDecision decision = await context.Service.AuthorizeAsync(
            context.Request with { ActionKey = string.Empty },
            TestContext.Current.CancellationToken);

        Assert.True(decision.IsUnavailable);
    }

    private static TestContextData Context(
        IReadOnlyList<ProductPolicyGrant> grants,
        bool active = true,
        bool auditReadBack = true,
        bool dependencyFailure = false)
    {
        IAuthorizationSubjectActivity activity = Substitute.For<IAuthorizationSubjectActivity>();
        IProductActionDescriptorRegistry descriptors = Substitute.For<IProductActionDescriptorRegistry>();
        IProductPolicyReadStore store = Substitute.For<IProductPolicyReadStore>();
        IAuthorizationAuditSink audit = Substitute.For<IAuthorizationAuditSink>();
        IAuthorizationUnitOfWork unitOfWork = Substitute.For<IAuthorizationUnitOfWork>();
        ProductAuthorizationRequest request = new(
            Guid.NewGuid(),
            SubjectReference.Human(Guid.NewGuid()),
            "record.read",
            "record",
            null,
            "correlation-1");
        activity.IsActiveAsync(
                request.WorkspaceId,
                request.Subject,
                Arg.Any<CancellationToken>())
            .Returns(active);
        descriptors.Find(request.ActionKey, request.ResourceType)
            .Returns(new ProductActionDescriptor(
                request.ActionKey,
                request.ResourceType,
                ProductActionKind.Record));
        store.ListActiveGrantsAsync(
                request.WorkspaceId,
                request.Subject,
                Arg.Any<CancellationToken>())
            .Returns(_ => dependencyFailure
                ? throw new InvalidOperationException("authorization dependency unavailable")
                : grants);
        AuditEventV1? captured = null;
        audit.IngestAsync(Arg.Any<AuditEventV1>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<AuditEventV1>();
                return new AuditIngestionResult(AuditIngestionDisposition.Stored, null);
            });
        audit.ReadBackAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => auditReadBack && captured is not null
                ? new AuditEventReadBackV1(
                    captured.EventId,
                    captured.ActorKind,
                    captured.ActorId,
                    captured.SubjectId,
                    captured.WorkspaceId,
                    captured.Action,
                    captured.TargetType,
                    captured.TargetId,
                    captured.Outcome,
                    captured.OccurredAt,
                    captured.CorrelationId,
                    captured.Metadata ?? new Dictionary<string, string>())
                : null);
        return new(
            new ProductAuthorizationService(
                activity,
                descriptors,
                store,
                audit,
                unitOfWork,
                new FixedClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z"))),
            store,
            audit,
            unitOfWork,
            request);
    }

    private sealed record TestContextData(
        ProductAuthorizationService Service,
        IProductPolicyReadStore Store,
        IAuthorizationAuditSink Audit,
        IAuthorizationUnitOfWork UnitOfWork,
        ProductAuthorizationRequest Request);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
