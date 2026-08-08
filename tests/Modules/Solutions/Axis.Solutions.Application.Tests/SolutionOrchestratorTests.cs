using Axis.Audit.Contracts;
using Axis.Solutions.Application;
using Axis.Solutions.Contracts;
using Axis.Solutions.Domain;

namespace Axis.Solutions.Application.Tests;

public sealed class SolutionOrchestratorTests
{
    [Fact]
    public async Task Orchestrator_PreflightFailure_PersistsAuditOnly()
    {
        Harness context = new(preflightFails: true);
        await Assert.ThrowsAsync<SolutionAdapterException>(() => context.Orchestrator.BeginInstallAsync(context.Request, TestContext.Current.CancellationToken));
        Assert.Empty(context.Installations.Items);
        Assert.Empty(context.Operations.Items);
        Assert.Equal(1, context.UnitOfWork.Saves);
        Assert.Contains(
            context.Audit.Events,
            value => value.EventType == "solutions.denied" && value.ProblemCode == "preflight");
    }

    [Fact]
    public async Task Orchestrator_WhenFinalTypedPreflightFails_CreatesNoInstallationWork()
    {
        Harness context = new(threeAdapters: true);

        await Assert.ThrowsAsync<SolutionAdapterException>(() =>
            context.Orchestrator.BeginInstallAsync(
                context.Request,
                TestContext.Current.CancellationToken));

        Assert.Equal([1, 1, 1], context.Adapters.Select(value => value.PreflightCalls));
        Assert.All(context.Adapters, value => Assert.Equal(0, value.ApplyCalls));
        Assert.Empty(context.Installations.Items);
        Assert.Empty(context.Operations.Items);
        Assert.Equal(1, context.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Orchestrator_WhenMultipleComponentsApply_AdvancesWithoutLeaseDelay()
    {
        Harness context = new(threeAdapters: true, finalPreflightFails: false);
        InstallSolutionResult started = await context.Orchestrator.BeginInstallAsync(
            context.Request,
            TestContext.Current.CancellationToken);

        SolutionOperationStatusDto status = started.Operation;
        foreach (int _ in Enumerable.Range(0, 3))
        {
            status = await context.Orchestrator.RunOnceAsync(
                started.Operation.Id,
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(SolutionOperationStatus.Succeeded, status.Status);
        Assert.All(status.Steps, step => Assert.Equal(SolutionStepStatus.Confirmed, step.Status));
        Assert.Equal([1, 1, 1], context.Adapters.Select(value => value.ApplyCalls));
        Assert.Equal([1L, 2L, 3L], context.Adapters.Select(value => value.LastReceipt!.LeaseEpoch));
    }

    [Fact]
    public async Task Orchestrator_ExactRetry_ReturnsExisting()
    {
        Harness context = new();
        InstallSolutionResult first = await context.Orchestrator.BeginInstallAsync(context.Request, TestContext.Current.CancellationToken);
        InstallSolutionResult retry = await context.Orchestrator.BeginInstallAsync(context.Request, TestContext.Current.CancellationToken);
        Assert.False(first.IsRetry); Assert.True(retry.IsRetry); Assert.Equal(first.Operation.Id, retry.Operation.Id);
        await Assert.ThrowsAsync<SolutionPackageException>(() => context.Orchestrator.BeginInstallAsync(context.Request with { RequestHash = new string('e', 64) }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Orchestrator_ReadbackConfirmation_AvoidsSecondApply()
    {
        Harness context = new();
        InstallSolutionResult started = await context.Orchestrator.BeginInstallAsync(context.Request, TestContext.Current.CancellationToken);
        context.Adapter.RetryableFailure = true;
        await context.Orchestrator.RunOnceAsync(started.Operation.Id, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        Assert.Equal(1, context.Adapter.ApplyCalls);
        Assert.Equal(context.Actor.SubjectId, context.Adapter.LastReceipt?.ActorSubjectId);
        Assert.Equal(context.Actor.SubjectKind, context.Adapter.LastReceipt?.ActorSubjectKind);
        Assert.Equal(context.Actor.CorrelationId, context.Adapter.LastReceipt?.CorrelationId);
        SolutionAuditEvent stepAudit = Assert.Single(
            context.Audit.Events,
            value => value.EventType == "solutions.install.step");
        Assert.Equal(AuditActorKindV1.System, stepAudit.ActorKind);
        Assert.Null(stepAudit.ActorId);
        Assert.Equal(context.Actor.SubjectId, stepAudit.SubjectId);
        Assert.Equal(context.Actor.CorrelationId, stepAudit.CorrelationId);
        Assert.Equal(context.Actor.SubjectKind, stepAudit.OriginatingSubjectKind);
        SolutionInstallationOperation operation = context.Operations.Items.Single();
        await context.Orchestrator.ResumeAsync(context.Actor, operation.Id, context.Now, TestContext.Current.CancellationToken);
        context.Adapter.ReadbackConfirmed = true;
        await context.Orchestrator.RunOnceAsync(operation.Id, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        Assert.Equal(1, context.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task Orchestrator_ServiceInitiatedInstall_PreservesActorAndCorrelation()
    {
        Harness context = new();
        SolutionActor serviceActor = context.Actor with { SubjectKind = SolutionSubjectKind.Service };

        await context.Orchestrator.BeginInstallAsync(
            context.Request with { Actor = serviceActor },
            TestContext.Current.CancellationToken);

        SolutionAuditEvent auditEvent = Assert.Single(
            context.Audit.Events,
            value => value.EventType == "solutions.install.requested");
        Assert.Equal(AuditActorKindV1.ServiceIdentity, auditEvent.ActorKind);
        Assert.Equal(serviceActor.SubjectId, auditEvent.ActorId);
        Assert.Equal(serviceActor.SubjectId, auditEvent.SubjectId);
        Assert.Equal(serviceActor.WorkspaceId, auditEvent.WorkspaceId);
        Assert.Equal(serviceActor.CorrelationId, auditEvent.CorrelationId);
        Assert.Null(auditEvent.OriginatingSubjectKind);
    }

    [Fact]
    public async Task Orchestrator_RevocationDenial_PersistsAudit()
    {
        Harness context = new();
        context.Keys.Active = false;
        await Assert.ThrowsAsync<SolutionPackageException>(() => context.Orchestrator.BeginInstallAsync(context.Request, TestContext.Current.CancellationToken));
        Assert.Empty(context.Installations.Items); Assert.Empty(context.Operations.Items);
        Assert.Contains(context.Audit.Events, x => x.EventType == "solutions.denied" && x.Outcome == "denied");
    }

    [Fact]
    public async Task Orchestrator_WhenPublisherIsRevokedBetweenSteps_BlocksBeforeNextMutation()
    {
        Harness context = new(threeAdapters: true, finalPreflightFails: false);
        InstallSolutionResult started = await context.Orchestrator.BeginInstallAsync(
            context.Request,
            TestContext.Current.CancellationToken);
        await context.Orchestrator.RunOnceAsync(
            started.Operation.Id,
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        context.Keys.Active = false;
        SolutionPackageException failure = await Assert.ThrowsAsync<SolutionPackageException>(() =>
            context.Orchestrator.RunOnceAsync(
                started.Operation.Id,
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken));

        Assert.Equal("solutions.package.publisher_untrusted", failure.ProblemCode);
        Assert.Equal([1, 0, 0], context.Adapters.Select(value => value.ApplyCalls));
        SolutionInstallation installation = Assert.Single(context.Installations.Items);
        Assert.Equal(ProvisioningStatus.Failed, installation.ProvisioningStatus);
        Assert.Equal(ComplianceStatus.Noncompliant, installation.ComplianceStatus);
        SolutionInstallationOperation operation = Assert.Single(context.Operations.Items);
        Assert.Equal(InstallationOperationStatus.Blocked, operation.Status);
        Assert.Equal(
            [InstallationStepStatus.Confirmed, InstallationStepStatus.Failed, InstallationStepStatus.Pending],
            operation.Steps.Select(value => value.Status));
        Assert.Contains(
            context.Audit.Events,
            value => value.EventType == "solutions.installation.noncompliant"
                && value.Outcome == "revoked"
                && value.ProblemCode == "solutions.package.publisher_untrusted");
    }

    [Fact]
    public async Task Orchestrator_TargetWorkspaceMismatch_DeniesBeforeLookup()
    {
        Harness context = new();
        await Assert.ThrowsAsync<SolutionPackageException>(() => context.Orchestrator.BeginInstallAsync(context.Request with { WorkspaceId = Guid.NewGuid() }, TestContext.Current.CancellationToken));
        Assert.Empty(context.Installations.Items);
        Assert.Equal("solutions.authorization.workspace_mismatch", Assert.Single(context.Audit.Events).ProblemCode);
    }

    [Fact]
    public async Task Orchestrator_AuditReadbackFailure_RecoversCanonically()
    {
        Harness context = new();
        context.Audit.ReadBackAvailable = false;

        SolutionPackageException failure = await Assert.ThrowsAsync<SolutionPackageException>(
            () => context.Orchestrator.BeginInstallAsync(
                context.Request,
                TestContext.Current.CancellationToken));

        Assert.Equal("solutions.audit.readback_failed", failure.ProblemCode);
        Assert.Single(context.Installations.Items);
        Assert.Single(context.Operations.Items);

        context.Audit.ReadBackAvailable = true;
        InstallSolutionResult retry = await context.Orchestrator.BeginInstallAsync(
            context.Request,
            TestContext.Current.CancellationToken);

        Assert.True(retry.IsRetry);
        Assert.Single(context.Installations.Items);
        Assert.Single(context.Operations.Items);
    }

    [Fact]
    public async Task VersionStatus_WhenComponentsDepend_ReturnsTopologicalPlan()
    {
        Harness context = new();
        context.Versions.Components =
        [
            new(
                "authorization.policy.v1",
                "dependent",
                new string('e', 64),
                [2],
                [new SolutionComponentReference("authorization.policy.v1", "root")]),
            new("authorization.policy.v1", "root", new string('d', 64), [1], []),
        ];

        SolutionVersionSummaryDto result = await context.Orchestrator.GetVersionStatusAsync(
            context.Actor,
            context.Versions.Version!.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(["root", "dependent"], result.Components.Select(value => value.Key));
        Assert.Equal("root", Assert.Single(result.Components[1].DependsOn).Key);
    }

    [Fact]
    public async Task InstallationStatus_AfterBegin_ReturnsDurableOperationLink()
    {
        Harness context = new();
        InstallSolutionResult started = await context.Orchestrator.BeginInstallAsync(
            context.Request,
            TestContext.Current.CancellationToken);

        SolutionInstallationStatusDto result = Assert.Single(
            await context.Orchestrator.ListInstallationStatusAsync(
                context.Actor,
                TestContext.Current.CancellationToken));

        Assert.Equal(started.Operation.Id, result.OperationId);
        Assert.Equal(SolutionOperationStatus.Pending, result.OperationStatus);
    }

    private sealed class Harness
    {
        public readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-07T00:00:00Z");
        public MemoryVersions Versions { get; } = new(); public MemoryInstallations Installations { get; } = new(); public MemoryOperations Operations { get; } = new(); public Keys Keys { get; } = new(); public Authority Authority { get; } = new(); public Audit Audit { get; } = new(); public UnitOfWork UnitOfWork { get; } = new(); public Adapter Adapter { get; }
        public IReadOnlyList<Adapter> Adapters { get; }
        public SolutionActor Actor { get; } = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test-correlation",
            SolutionSubjectKind.Human);
        public SolutionOrchestrator Orchestrator { get; }
        public InstallSolutionRequest Request { get; }
        public Harness(
            bool preflightFails = false,
            bool threeAdapters = false,
            bool finalPreflightFails = true)
        {
            Adapters = threeAdapters
                ?
                [
                    new("authorization.policy.v1", false),
                    new("rule.binding.v1", false),
                    new("business-object.definition.v1", finalPreflightFails),
                ]
                : [new("authorization.policy.v1", preflightFails)];
            Adapter = Adapters[0];
            SolutionVersion version = SolutionVersion.Create("reference_application", "0.1.0", new string('a', 64), [1], new string('b', 64), "axis", "release_key", new string('c', 40), "build", Now, new Uri("https://example.test/reference"), Now);
            Versions.Version = version;
            Versions.Components = Adapters
                .Select((value, index) => new VerifiedSolutionComponent(
                    value.ComponentType,
                    $"component-{index}",
                    new string((char)('d' + index), 64),
                    [(byte)(index + 1)],
                    []))
                .ToArray();
            Orchestrator = new(new SolutionPackageVerifier(Keys), Versions, Installations, Operations, Keys, Authority, Audit, UnitOfWork, Adapters, new Clock(Now));
            Request = new(Actor, Actor.WorkspaceId, version.Id, "request-1", new string('f', 64), Now);
        }
    }
    private sealed class MemoryVersions : ISolutionVersionRepository
    {
        public SolutionVersion? Version; public IReadOnlyList<VerifiedSolutionComponent> Components = [];
        public Task AddAsync(SolutionVersion version, IReadOnlyList<VerifiedSolutionComponent> components, CancellationToken cancellationToken = default) { Version = version; Components = components; return Task.CompletedTask; }
        public Task<SolutionVersion?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Version?.Id == id ? Version : null);
        public Task<SolutionVersion?> FindByIdentityAsync(string key, string version, CancellationToken cancellationToken = default) => Task.FromResult(Version?.SolutionKey == key && Version.Version == version ? Version : null);
        public Task<IReadOnlyList<VerifiedSolutionComponent>> GetComponentsAsync(Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult(Components);
    }
    private sealed class MemoryInstallations : ISolutionInstallationRepository
    {
        public List<SolutionInstallation> Items { get; } = [];
        public Task AddAsync(SolutionInstallation value, CancellationToken cancellationToken = default) { Items.Add(value); return Task.CompletedTask; }
        public Task<SolutionInstallation?> FindAsync(Guid workspaceId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult(Items.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.SolutionVersionId == versionId));
        public Task<SolutionInstallation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<SolutionInstallation>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SolutionInstallation>>(Items.Where(x => x.WorkspaceId == workspaceId).ToArray());
        public Task<IReadOnlyList<SolutionInstallation>> ListByPublisherKeyAsync(string publisherId, string keyId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SolutionInstallation>>(Items);
    }
    private sealed class MemoryOperations : ISolutionOperationRepository
    {
        public List<SolutionInstallationOperation> Items { get; } = [];
        public Task AddAsync(SolutionInstallationOperation value, CancellationToken cancellationToken = default) { Items.Add(value); return Task.CompletedTask; }
        public Task<SolutionInstallationOperation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
        public Task<SolutionInstallationOperation?> FindByIdempotencyAsync(Guid workspaceId, string idempotencyKey, CancellationToken cancellationToken = default) => Task.FromResult(Items.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey));
        public Task<IReadOnlyList<SolutionInstallationOperation>> ListByInstallationAsync(Guid installationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SolutionInstallationOperation>>(Items.Where(x => x.InstallationId == installationId).ToArray());
    }
    private sealed class Keys : ITrustedPublisherKeyReader { public bool Active { get; set; } = true; public Task<TrustedPublisherSnapshot?> FindAsync(string publisherId, string keyId, CancellationToken cancellationToken = default) => Task.FromResult<TrustedPublisherSnapshot?>(new(publisherId, keyId, string.Empty, Active, !Active, 1)); }
    private sealed class Authority : ISolutionAuthority { public bool Allowed { get; set; } = true; public Task DemandAsync(SolutionActor actor, Guid targetWorkspaceId, SolutionAuthorityAction action, CancellationToken cancellationToken = default) => Allowed ? Task.CompletedTask : Task.FromException(new SolutionPackageException("solutions.authorization.denied")); }
    private sealed class Audit : ISolutionsAuditOutbox
    {
        public List<SolutionAuditEvent> Events { get; } = [];
        public bool ReadBackAvailable { get; set; } = true;
        public Task EnqueueAsync(SolutionAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReadBackAvailable && Events.Any(value => value.EventId == eventId));
    }
    private sealed class UnitOfWork : ISolutionsUnitOfWork { public int Saves; public Task SaveChangesAsync(CancellationToken cancellationToken = default) { Saves++; return Task.CompletedTask; } }
    private sealed class Adapter(string componentType, bool fail) : ISolutionComponentAdapter
    {
        public string ComponentType => componentType; public int ApplyCalls; public int PreflightCalls; public bool ReadbackConfirmed; public bool RetryableFailure; public SolutionApplyReceipt? LastReceipt;
        public Task PreflightAsync(Guid workspaceId, SolutionAdapterPreflight component, CancellationToken cancellationToken = default) { PreflightCalls++; return fail ? Task.FromException(new SolutionAdapterException("preflight", false)) : Task.CompletedTask; }
        public Task<SolutionAdapterReadback> ReadBackAsync(Guid workspaceId, SolutionAdapterPreflight component, SolutionApplyReceipt receipt, CancellationToken cancellationToken = default) { LastReceipt = receipt; return Task.FromResult(new SolutionAdapterReadback(ReadbackConfirmed || ApplyCalls > 0 && !RetryableFailure, false)); }
        public Task ApplyAsync(Guid workspaceId, SolutionAdapterPreflight component, SolutionApplyReceipt receipt, CancellationToken cancellationToken = default) { ApplyCalls++; return RetryableFailure ? Task.FromException(new SolutionAdapterException("response_lost", true)) : Task.CompletedTask; }
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
