using System.Text.Json;
using Axis.Audit.Contracts;
using Axis.Authorization.Application;
using Axis.Authorization.Contracts;
using Axis.Authorization.Infrastructure.Persistence;
using Axis.Authorization.Infrastructure.Tests.Fixtures;
using Axis.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Axis.Authorization.Infrastructure.Tests;

[Collection("AuthorizationDb")]
public sealed class ProductPolicyInstallerPersistenceTests(AuthorizationDatabaseFixture database)
{
    [Fact]
    public async Task Install_ValidPolicy_PersistsCanonicalAudit()
    {
        InstallProductPolicyRequest request = Request();

        ProductPolicyInstallResult result = await InstallAsync(request);

        Assert.True(result.IsInstalled);
        await using AuthorizationDbContext read = database.CreateContext();
        InstalledPolicyRow policy = await read.Policies.SingleAsync(
            value => value.WorkspaceId == request.WorkspaceId,
            TestContext.Current.CancellationToken);
        ProductPolicyComponent component = JsonSerializer.Deserialize<ProductPolicyComponent>(
            policy.CanonicalContent,
            ProductPolicyJson.Options)
            ?? throw new InvalidOperationException("Expected product policy payload.");
        Assert.Equal("reference", component.PolicyKey);
        Assert.Contains("\"leaseEpoch\":2", policy.Provenance, StringComparison.Ordinal);
        AuthorizationAuditOutboxRow audit = Assert.Single(
            await read.AuditOutbox.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken),
            row => (JsonSerializer.Deserialize<AuditEventV1>(row.Payload)
                ?? throw new InvalidOperationException("Expected audit payload.")).WorkspaceId == request.WorkspaceId);
        AuditEventV1 persisted = JsonSerializer.Deserialize<AuditEventV1>(audit.Payload)
            ?? throw new InvalidOperationException("Expected audit payload.");
        Assert.Equal(request.Component.VersionId, persisted.TargetId);
        Assert.Equal("authorization.policy_install", persisted.Action);
        Assert.Equal(AuditActorKindV1.System, persisted.ActorKind);
        Assert.Null(persisted.ActorId);
        Assert.Equal(request.OriginatingSubject.Id, persisted.SubjectId);
        Assert.Equal(request.CorrelationId, persisted.CorrelationId);
        Assert.Equal(
            request.OriginatingSubject.Kind.ToString(),
            persisted.Metadata!["originating_subject_kind"]);
        Assert.NotNull(audit.ReadBackAt);
    }

    [Fact]
    public async Task Install_MissingAuditReadBack_RollsBackPolicy()
    {
        InstallProductPolicyRequest request = Request();

        ProductPolicyInstallResult result = await InstallAsync(request, missingReadBack: true);

        Assert.False(result.IsInstalled);
        Assert.Equal("audit_unavailable", result.Error);
        await using AuthorizationDbContext read = database.CreateContext();
        Assert.Equal(0, await read.Policies.CountAsync(
            value => value.WorkspaceId == request.WorkspaceId,
            TestContext.Current.CancellationToken));
        Assert.DoesNotContain(
            await read.AuditOutbox.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken),
            row => (JsonSerializer.Deserialize<AuditEventV1>(row.Payload)
                ?? throw new InvalidOperationException("Expected audit payload.")).WorkspaceId == request.WorkspaceId);
    }

    [Fact]
    public async Task Install_EquivalentRetry_ReturnsPersistedPolicy()
    {
        InstallProductPolicyRequest request = Request();

        ProductPolicyInstallResult initial = await InstallAsync(request);
        ProductPolicyInstallResult retry = await InstallAsync(request);

        Assert.True(initial.IsInstalled);
        Assert.True(retry.IsInstalled);
        await using AuthorizationDbContext read = database.CreateContext();
        Assert.Equal(1, await read.Policies.CountAsync(
            value => value.WorkspaceId == request.WorkspaceId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Install_HigherLeaseEpoch_AdvancesDurableReceiptWithoutChangingContent()
    {
        InstallProductPolicyRequest request = Request();
        ProductPolicyInstallResult initial = await InstallAsync(request);
        await using AuthorizationDbContext initialRead = database.CreateContext();
        string originalContent = await initialRead.Policies.AsNoTracking()
            .Where(value => value.WorkspaceId == request.WorkspaceId)
            .Select(value => value.CanonicalContent)
            .SingleAsync(TestContext.Current.CancellationToken);

        ProductPolicyInstallResult recovery = await InstallAsync(request with { LeaseEpoch = 3 });

        Assert.True(initial.IsInstalled);
        Assert.True(recovery.IsInstalled);
        await using AuthorizationDbContext read = database.CreateContext();
        InstalledPolicyRow persisted = await read.Policies.AsNoTracking().SingleAsync(
            value => value.WorkspaceId == request.WorkspaceId,
            TestContext.Current.CancellationToken);
        Assert.Equal(originalContent, persisted.CanonicalContent);
        Assert.Contains("\"solutionVersion\":\"1.0.0\"", persisted.Provenance, StringComparison.Ordinal);
        Assert.Contains($"\"componentHash\":\"{new string('a', 64)}\"", persisted.Provenance, StringComparison.Ordinal);
        Assert.Contains("\"operation\":\"operation-1\"", persisted.Provenance, StringComparison.Ordinal);
        Assert.Contains("\"step\":\"step-1\"", persisted.Provenance, StringComparison.Ordinal);
        Assert.Contains("\"leaseEpoch\":3", persisted.Provenance, StringComparison.Ordinal);
        ProductPolicyInstaller reader = CreateInstaller(read);
        ProductPolicyComponentReadBack? receipt = await reader.ReadBackAsync(
            request.WorkspaceId,
            request.Component.VersionId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(receipt);
        Assert.Equal(request.ComponentHash, receipt.ComponentHash);
        Assert.Equal(request.OperationId, receipt.OperationId);
        Assert.Equal(request.StepId, receipt.StepId);
        Assert.Equal(3, receipt.LeaseEpoch);
        Assert.Equal(2, await CountAuditsAsync(read, request.WorkspaceId));
    }

    [Fact]
    public async Task Install_StaleOrMismatchedReceipt_PreservesDurableReceipt()
    {
        InstallProductPolicyRequest request = Request();
        Assert.True((await InstallAsync(request)).IsInstalled);

        ProductPolicyInstallResult stale = await InstallAsync(request with { LeaseEpoch = 1 });
        ProductPolicyInstallResult mismatched = await InstallAsync(request with
        {
            OperationId = "operation-2",
            LeaseEpoch = 3,
        });

        Assert.False(stale.IsInstalled);
        Assert.Equal("authorization.policy_stale_receipt", stale.Error);
        Assert.False(mismatched.IsInstalled);
        Assert.Equal("authorization.policy_receipt_conflict", mismatched.Error);
        await using AuthorizationDbContext read = database.CreateContext();
        InstalledPolicyRow persisted = await read.Policies.AsNoTracking().SingleAsync(
            value => value.WorkspaceId == request.WorkspaceId,
            TestContext.Current.CancellationToken);
        Assert.Contains("\"operation\":\"operation-1\"", persisted.Provenance, StringComparison.Ordinal);
        Assert.Contains("\"leaseEpoch\":2", persisted.Provenance, StringComparison.Ordinal);
        Assert.Equal(1, await CountAuditsAsync(read, request.WorkspaceId));
    }

    private static async Task<int> CountAuditsAsync(
        AuthorizationDbContext context,
        Guid workspaceId) =>
        (await context.AuditOutbox.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken))
            .Count(row => (JsonSerializer.Deserialize<AuditEventV1>(row.Payload)
                ?? throw new InvalidOperationException("Expected audit payload.")).WorkspaceId == workspaceId);

    private async Task<ProductPolicyInstallResult> InstallAsync(
        InstallProductPolicyRequest request,
        bool missingReadBack = false)
    {
        await using AuthorizationDbContext context = database.CreateContext();
        FixedClock clock = new(DateTimeOffset.Parse("2026-08-07T00:00:00Z"));
        AuthorizationAuditOutbox outbox = new(context, clock);
        IAuthorizationAuditSink audit = missingReadBack ? new MissingReadBackAuditSink(outbox) : outbox;
        ProductPolicyInstaller installer = CreateInstaller(context, audit, clock);
        return await installer.InstallAsync(request, TestContext.Current.CancellationToken);
    }

    private static ProductPolicyInstaller CreateInstaller(
        AuthorizationDbContext context,
        IAuthorizationAuditSink? audit = null,
        FixedClock? clock = null)
    {
        clock ??= new FixedClock(DateTimeOffset.Parse("2026-08-07T00:00:00Z"));
        return new(
            new InstalledProductPolicyStore(context),
            new Descriptors(),
            audit ?? new AuthorizationAuditOutbox(context, clock),
            new AuthorizationUnitOfWork(context),
            clock);
    }

    private static InstallProductPolicyRequest Request() => new(
        Guid.NewGuid(),
        new(
            "reference",
            Guid.NewGuid(),
            [new("Applicant", new Dictionary<string, ProductRolePresentation>
            {
                ["en"] = new("Applicant", null),
            })],
            [new("Applicant", "record.read", "record", null, ProductActionScope.Own)]),
        "1.0.0",
        new string('a', 64),
        "operation-1",
        "step-1",
        2,
        SubjectReference.Human(Guid.NewGuid()),
        "policy-install-test");

    private sealed class Descriptors : IProductActionDescriptorRegistry
    {
        public ProductActionDescriptor? Find(string actionKey, string resourceType) =>
            actionKey == "record.read" && resourceType == "record"
                ? new(actionKey, resourceType, ProductActionKind.Record)
                : null;
    }

    private sealed class MissingReadBackAuditSink(AuthorizationAuditOutbox inner) : IAuthorizationAuditSink
    {
        public Task<AuditIngestionResult> IngestAsync(AuditEventV1 auditEvent, CancellationToken cancellationToken = default) =>
            inner.IngestAsync(auditEvent, cancellationToken);

        public Task<AuditEventReadBackV1?> ReadBackAsync(Guid eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AuditEventReadBackV1?>(null);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
