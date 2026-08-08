using Axis.Audit.Contracts;
using Axis.Authorization.Application;
using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using NSubstitute;

namespace Axis.Authorization.Application.Tests;

public sealed class ProductPolicyInstallerTests
{
    [Fact]
    public async Task Install_ValidPolicy_CommitsAuditedCanonicalPolicy()
    {
        Context context = Create();

        ProductPolicyInstallResult result = await context.Installer.InstallAsync(context.Request, TestContext.Current.CancellationToken);

        Assert.True(result.IsInstalled);
        await context.Store.Received(1).AddAsync(
            Arg.Is<StoredProductPolicy>(value =>
                value.CanonicalContent.Contains("\"policyKey\":\"reference\"", StringComparison.Ordinal) &&
                value.Provenance.Contains("\"leaseEpoch\":4", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await context.UnitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await context.Audit.Received(1).IngestAsync(
            Arg.Is<AuditEventV1>(value =>
                value.ActorKind == AuditActorKindV1.System &&
                value.ActorId == null &&
                value.SubjectId == context.Request.OriginatingSubject.Id &&
                value.CorrelationId == context.Request.CorrelationId &&
                value.Metadata!["originating_subject_kind"] == SubjectKind.Human.ToString()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_ReorderedEquivalentPolicy_ReturnsCanonicalRetry()
    {
        Context context = Create();
        ProductPolicyInstallResult initial = await context.Installer.InstallAsync(context.Request, TestContext.Current.CancellationToken);
        StoredProductPolicy policy = context.Store.ReceivedCalls()
            .Select(call => call.GetArguments().FirstOrDefault())
            .OfType<StoredProductPolicy>()
            .Single();
        context.Store.GetAsync(context.Request.WorkspaceId, context.Request.Component.VersionId, Arg.Any<CancellationToken>())
            .Returns(policy);

        ProductPolicyComponent reordered = context.Request.Component with
        {
            Roles = context.Request.Component.Roles.Reverse().ToArray(),
            Grants = context.Request.Component.Grants.Reverse().ToArray(),
        };
        ProductPolicyInstallResult retry = await context.Installer.InstallAsync(context.Request with { Component = reordered }, TestContext.Current.CancellationToken);

        Assert.True(initial.IsInstalled);
        Assert.True(retry.IsInstalled);
        await context.Store.Received(1).AddAsync(Arg.Any<StoredProductPolicy>(), Arg.Any<CancellationToken>());
        await context.Store.DidNotReceive().TryUpdateProvenanceAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_HigherLeaseEpochForSameReceipt_AdvancesOnlyProvenance()
    {
        Context context = Create();
        await context.Installer.InstallAsync(context.Request, TestContext.Current.CancellationToken);
        StoredProductPolicy policy = AddedPolicy(context);
        context.Store.GetAsync(context.Request.WorkspaceId, context.Request.Component.VersionId, Arg.Any<CancellationToken>())
            .Returns(policy);

        ProductPolicyInstallResult result = await context.Installer.InstallAsync(
            context.Request with { LeaseEpoch = context.Request.LeaseEpoch + 1 },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsInstalled);
        await context.Store.Received(1).TryUpdateProvenanceAsync(
            context.Request.WorkspaceId,
            context.Request.Component.VersionId,
            policy.Provenance,
            Arg.Is<string>(value => value.Contains("\"leaseEpoch\":5", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await context.Store.Received(1).AddAsync(Arg.Any<StoredProductPolicy>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_LowerLeaseEpoch_RejectsStaleReceipt()
    {
        Context context = Create();
        await context.Installer.InstallAsync(context.Request, TestContext.Current.CancellationToken);
        StoredProductPolicy policy = AddedPolicy(context);
        context.Store.GetAsync(context.Request.WorkspaceId, context.Request.Component.VersionId, Arg.Any<CancellationToken>())
            .Returns(policy);

        ProductPolicyInstallResult result = await context.Installer.InstallAsync(
            context.Request with { LeaseEpoch = context.Request.LeaseEpoch - 1 },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
        Assert.Equal("authorization.policy_stale_receipt", result.Error);
        await context.Store.DidNotReceive().TryUpdateProvenanceAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("solution")]
    [InlineData("component")]
    [InlineData("operation")]
    [InlineData("step")]
    public async Task Install_DifferentReceiptIdentity_RejectsProvenanceConflict(string changedField)
    {
        Context context = Create();
        await context.Installer.InstallAsync(context.Request, TestContext.Current.CancellationToken);
        StoredProductPolicy policy = AddedPolicy(context);
        context.Store.GetAsync(context.Request.WorkspaceId, context.Request.Component.VersionId, Arg.Any<CancellationToken>())
            .Returns(policy);
        InstallProductPolicyRequest changed = changedField switch
        {
            "solution" => context.Request with { SolutionVersion = "1.0.1", LeaseEpoch = 5 },
            "component" => context.Request with { ComponentHash = new string('b', 64), LeaseEpoch = 5 },
            "operation" => context.Request with { OperationId = "operation-2", LeaseEpoch = 5 },
            "step" => context.Request with { StepId = "step-2", LeaseEpoch = 5 },
            _ => throw new InvalidOperationException("Unknown changed receipt field."),
        };

        ProductPolicyInstallResult result = await context.Installer.InstallAsync(
            changed,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
        Assert.Equal("authorization.policy_receipt_conflict", result.Error);
        await context.Store.DidNotReceive().TryUpdateProvenanceAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_ChangedPolicy_RejectsImmutableVersion()
    {
        Context context = Create();
        await context.Installer.InstallAsync(context.Request, TestContext.Current.CancellationToken);
        StoredProductPolicy policy = context.Store.ReceivedCalls()
            .Select(call => call.GetArguments().FirstOrDefault())
            .OfType<StoredProductPolicy>()
            .Single();
        context.Store.GetAsync(context.Request.WorkspaceId, context.Request.Component.VersionId, Arg.Any<CancellationToken>())
            .Returns(policy);

        ProductPolicyInstallResult result = await context.Installer.InstallAsync(
            context.Request with { Component = context.Request.Component with { PolicyKey = "changed" } },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
        Assert.Equal("authorization.policy_immutable", result.Error);
    }

    [Fact]
    public async Task Install_ExactRetryWithoutOriginalAudit_FailsClosed()
    {
        Context context = Create();
        await context.Installer.InstallAsync(context.Request, TestContext.Current.CancellationToken);
        StoredProductPolicy policy = AddedPolicy(context);
        context.Store.GetAsync(context.Request.WorkspaceId, context.Request.Component.VersionId, Arg.Any<CancellationToken>())
            .Returns(policy);
        context.Audit.ReadBackAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((AuditEventReadBackV1?)null);

        ProductPolicyInstallResult result = await context.Installer.InstallAsync(
            context.Request,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
        Assert.Equal("audit_unavailable", result.Error);
        await context.UnitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_HigherLeaseEpochWhenReceiptChangedConcurrently_ReturnsConflict()
    {
        Context context = Create();
        await context.Installer.InstallAsync(context.Request, TestContext.Current.CancellationToken);
        StoredProductPolicy policy = AddedPolicy(context);
        context.Store.GetAsync(context.Request.WorkspaceId, context.Request.Component.VersionId, Arg.Any<CancellationToken>())
            .Returns(policy);
        context.Store.TryUpdateProvenanceAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(false);

        ProductPolicyInstallResult result = await context.Installer.InstallAsync(
            context.Request with { LeaseEpoch = 5 },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
        Assert.Equal("conflict", result.Error);
        await context.UnitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
        await context.UnitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_AuditReadBackMissing_RollsBackPolicy()
    {
        Context context = Create(readBack: false);

        ProductPolicyInstallResult result = await context.Installer.InstallAsync(context.Request, TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
        Assert.Equal("audit_unavailable", result.Error);
        await context.UnitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await context.UnitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_ZeroLeaseEpoch_RejectsRequest()
    {
        Context context = Create();

        ProductPolicyInstallResult result = await context.Installer.InstallAsync(
            context.Request with { LeaseEpoch = 0 },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
        Assert.Equal("authorization.policy_invalid", result.Error);
        await context.Store.DidNotReceive().AddAsync(
            Arg.Any<StoredProductPolicy>(),
            Arg.Any<CancellationToken>());
    }

    private static Context Create(bool readBack = true)
    {
        IInstalledProductPolicyStore store = Substitute.For<IInstalledProductPolicyStore>();
        IProductActionDescriptorRegistry descriptors = Substitute.For<IProductActionDescriptorRegistry>();
        IAuthorizationAuditSink audit = Substitute.For<IAuthorizationAuditSink>();
        IAuthorizationUnitOfWork unitOfWork = Substitute.For<IAuthorizationUnitOfWork>();
        store.TryUpdateProvenanceAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        ProductPolicyComponent component = new(
            "reference",
            Guid.NewGuid(),
            [
                new("Applicant", new Dictionary<string, ProductRolePresentation>
                {
                    ["en"] = new("Applicant", null),
                }),
                new("Caseworker", new Dictionary<string, ProductRolePresentation>
                {
                    ["en"] = new("Caseworker", null),
                }),
            ],
            [
                new("Applicant", "record.read", "record", null, ProductActionScope.Own),
                new("Caseworker", "record.read", "record", null, ProductActionScope.All),
            ]);
        foreach (ProductPolicyGrant grant in component.Grants)
            descriptors.Find(grant.ActionKey, grant.ResourceType)
                .Returns(new ProductActionDescriptor(grant.ActionKey, grant.ResourceType, ProductActionKind.Record));
        audit.IngestAsync(Arg.Any<AuditEventV1>(), Arg.Any<CancellationToken>())
            .Returns(new AuditIngestionResult(AuditIngestionDisposition.Stored, null));
        if (readBack)
        {
            AuditEventV1? staged = null;
            audit.IngestAsync(Arg.Any<AuditEventV1>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    staged = call.Arg<AuditEventV1>();
                    return new AuditIngestionResult(AuditIngestionDisposition.Stored, null);
                });
            audit.ReadBackAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(_ => staged is null ? null : ReadBack(staged));
        }

        InstallProductPolicyRequest request = new(
            Guid.NewGuid(), component, "1.0.0", new string('a', 64), "operation-1", "step-1", 4,
            SubjectReference.Human(Guid.NewGuid()), "policy-install-test");
        return new(
            new ProductPolicyInstaller(store, descriptors, audit, unitOfWork,
                new FixedClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z"))),
            store,
            audit,
            unitOfWork,
            request);
    }

    private static AuditEventReadBackV1 ReadBack(AuditEventV1 value) => new(
        value.EventId, value.ActorKind, value.ActorId, value.SubjectId, value.WorkspaceId,
        value.Action, value.TargetType, value.TargetId, value.Outcome, value.OccurredAt,
        value.CorrelationId, value.Metadata ?? new Dictionary<string, string>());

    private static StoredProductPolicy AddedPolicy(Context context) => context.Store.ReceivedCalls()
        .Select(call => call.GetArguments().FirstOrDefault())
        .OfType<StoredProductPolicy>()
        .Single();

    private sealed record Context(
        ProductPolicyInstaller Installer,
        IInstalledProductPolicyStore Store,
        IAuthorizationAuditSink Audit,
        IAuthorizationUnitOfWork UnitOfWork,
        InstallProductPolicyRequest Request);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
