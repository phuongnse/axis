using Axis.Audit.Contracts;
using Axis.Solutions.Application;
using Axis.Solutions.Contracts;
using Axis.Solutions.Domain;
using System.Security.Cryptography;
using System.Text;

namespace Axis.Solutions.Application.Tests;

public sealed class SolutionOrchestratorTests
{
    [Fact]
    public async Task Orchestrator_ExactPublishPersistenceRace_ReturnsCanonicalVersion()
    {
        const string digest = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Keys keys = new() { PublicKeyPem = key.ExportSubjectPublicKeyInfoPem() };
        MemoryVersions versions = new();
        MemoryInstallations installations = new();
        MemoryOperations operations = new();
        Audit audit = new();
        UnitOfWork unitOfWork = new();
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-07T00:00:00Z");
        SolutionActor actor = new(Guid.NewGuid(), Guid.NewGuid(), "publish-race", SolutionSubjectKind.Human);
        byte[] envelope = CreateSignedEnvelope(key, digest);
        Guid canonicalVersionId = Guid.Empty;
        unitOfWork.OnNextSave = () =>
        {
            SolutionVersion proposed = versions.Version!;
            SolutionVersion canonical = SolutionVersion.Create(
                proposed.SolutionKey,
                proposed.Version,
                proposed.PackageSha256,
                proposed.Envelope,
                proposed.AxisOpenApiSha256,
                proposed.PublisherId,
                proposed.PublisherKeyId,
                proposed.SourceRevision,
                proposed.BuildId,
                proposed.BuiltAt,
                new Uri(proposed.SourceUri),
                proposed.PublishedAt);
            canonicalVersionId = canonical.Id;
            versions.Version = canonical;
            return new SolutionPersistenceException(
                "solutions.persistence.version_identity_conflict",
                new InvalidOperationException("simulated concurrent winner"));
        };
        SolutionOrchestrator orchestrator = new(
            new SolutionPackageVerifier(keys),
            versions,
            installations,
            operations,
            keys,
            new Digest(digest),
            new Authority(),
            audit,
            unitOfWork,
            [],
            new Clock(now));

        PublishSolutionResult result = await orchestrator.PublishAsync(
            new PublishSolutionRequest(actor, envelope, now),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsRetry);
        Assert.Equal(canonicalVersionId, result.Version.Id);
        Assert.Contains(audit.Events, value =>
            value.EventType == "solutions.version.publish_retried"
            && value.Outcome == "canonical_retry");
    }

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
    public async Task Orchestrator_DifferentVersionOfInstalledSolution_RejectsBeforeAdapterMutation()
    {
        Harness context = new();
        await context.Orchestrator.BeginInstallAsync(
            context.Request,
            TestContext.Current.CancellationToken);
        SolutionVersion otherVersion = SolutionVersion.Create(
            context.Versions.Version!.SolutionKey,
            "0.2.0",
            new string('1', 64),
            [2],
            new string('b', 64),
            "axis",
            "release_key",
            new string('2', 40),
            "other-build",
            context.Now,
            new Uri("https://example.test/reference"),
            context.Now);
        context.Versions.Version = otherVersion;

        SolutionPackageException failure = await Assert.ThrowsAsync<SolutionPackageException>(() =>
            context.Orchestrator.BeginInstallAsync(
                context.Request with
                {
                    SolutionVersionId = otherVersion.Id,
                    IdempotencyKey = "request-2",
                    RequestHash = new string('2', 64),
                },
                TestContext.Current.CancellationToken));

        Assert.Equal("solutions.install.already_exists", failure.ProblemCode);
        Assert.Equal(1, context.Adapter.PreflightCalls);
        Assert.Equal(0, context.Adapter.ApplyCalls);
        Assert.Single(context.Installations.Items);
    }

    [Fact]
    public async Task Orchestrator_ExactInstallPersistenceRace_ReturnsCanonicalOperation()
    {
        Harness context = new();
        Guid canonicalOperationId = Guid.Empty;
        context.UnitOfWork.OnNextSave = () =>
        {
            context.Installations.Items.Clear();
            context.Operations.Items.Clear();
            SolutionInstallation installation = SolutionInstallation.Create(
                context.Actor.WorkspaceId,
                context.Versions.Version!.SolutionKey,
                context.Versions.Version.Id,
                context.Now);
            SolutionInstallationOperation operation = SolutionInstallationOperation.Create(
                context.Actor.WorkspaceId,
                context.Actor.SubjectId,
                context.Actor.SubjectKind,
                context.Actor.CorrelationId,
                installation.Id,
                context.Request.IdempotencyKey,
                context.Request.RequestHash,
                [new("authorization.policy.v1", "component-0", new string('d', 64), [])],
                context.Now);
            canonicalOperationId = operation.Id;
            context.Installations.Items.Add(installation);
            context.Operations.Items.Add(operation);
            return new SolutionPersistenceException(
                "solutions.persistence.operation_idempotency_conflict",
                new InvalidOperationException("simulated concurrent winner"));
        };

        InstallSolutionResult result = await context.Orchestrator.BeginInstallAsync(
            context.Request,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsRetry);
        Assert.Equal(canonicalOperationId, result.Operation.Id);
        Assert.Equal(0, context.Adapter.ApplyCalls);
        Assert.Single(context.Installations.Items);
        Assert.Single(context.Operations.Items);
    }

    [Fact]
    public async Task Orchestrator_DifferentVersionPersistenceRace_ConflictsBeforeAdapterMutation()
    {
        Harness context = new();
        SolutionVersion requestedVersion = context.Versions.Version!;
        SolutionVersion winningVersion = SolutionVersion.Create(
            requestedVersion.SolutionKey,
            "0.2.0",
            new string('1', 64),
            [2],
            requestedVersion.AxisOpenApiSha256,
            requestedVersion.PublisherId,
            requestedVersion.PublisherKeyId,
            new string('2', 40),
            "winning-build",
            context.Now,
            new Uri("https://example.test/reference"),
            context.Now);
        context.UnitOfWork.OnNextSave = () =>
        {
            context.Installations.Items.Clear();
            context.Operations.Items.Clear();
            context.Installations.Items.Add(SolutionInstallation.Create(
                context.Actor.WorkspaceId,
                winningVersion.SolutionKey,
                winningVersion.Id,
                context.Now));
            return new SolutionPersistenceException(
                "solutions.persistence.installation_solution_conflict",
                new InvalidOperationException("simulated concurrent winner"));
        };

        SolutionPackageException failure = await Assert.ThrowsAsync<SolutionPackageException>(() =>
            context.Orchestrator.BeginInstallAsync(
                context.Request,
                TestContext.Current.CancellationToken));

        Assert.Equal("solutions.install.already_exists", failure.ProblemCode);
        Assert.Equal(0, context.Adapter.ApplyCalls);
        Assert.Single(context.Installations.Items);
        Assert.Empty(context.Operations.Items);
    }

    [Fact]
    public async Task Orchestrator_WhenAxisOpenApiDigestChanged_RejectsBeginBeforePreflight()
    {
        Harness context = new();
        context.Digest.Value = new string('9', 64);

        SolutionPackageException failure = await Assert.ThrowsAsync<SolutionPackageException>(() =>
            context.Orchestrator.BeginInstallAsync(
                context.Request,
                TestContext.Current.CancellationToken));

        Assert.Equal("solutions.package.axis_openapi_mismatch", failure.ProblemCode);
        Assert.Equal(0, context.Adapter.PreflightCalls);
        Assert.Empty(context.Installations.Items);
    }

    [Fact]
    public async Task Resume_WhenOperationIsNotFailed_DeniesWithoutTransition()
    {
        Harness context = new();
        InstallSolutionResult started = await context.Orchestrator.BeginInstallAsync(
            context.Request,
            TestContext.Current.CancellationToken);

        SolutionPackageException failure = await Assert.ThrowsAsync<SolutionPackageException>(() =>
            context.Orchestrator.ResumeAsync(
                context.Actor,
                started.Operation.Id,
                context.Now,
                TestContext.Current.CancellationToken));

        Assert.Equal("solutions.install.operation_not_resumable", failure.ProblemCode);
        Assert.DoesNotContain(context.Audit.Events, value => value.EventType == "solutions.install.resumed");
        Assert.Contains(
            context.Audit.Events,
            value => value.EventType == "solutions.denied"
                && value.Outcome == "denied"
                && value.ProblemCode == "solutions.install.operation_not_resumable");
    }

    [Fact]
    public async Task Orchestrator_DigestChangedBeforeResume_RejectsFailedOperation()
    {
        Harness context = new();
        InstallSolutionResult started = await context.Orchestrator.BeginInstallAsync(
            context.Request,
            TestContext.Current.CancellationToken);
        context.Adapter.RetryableFailure = true;
        await context.Orchestrator.RunOnceAsync(
            started.Operation.Id,
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);
        context.Digest.Value = new string('9', 64);

        SolutionPackageException failure = await Assert.ThrowsAsync<SolutionPackageException>(() =>
            context.Orchestrator.ResumeAsync(
                context.Actor,
                started.Operation.Id,
                context.Now,
                TestContext.Current.CancellationToken));

        Assert.Equal("solutions.package.axis_openapi_mismatch", failure.ProblemCode);
        Assert.DoesNotContain(context.Audit.Events, value => value.EventType == "solutions.install.resumed");
        Assert.Equal(InstallationOperationStatus.Failed, context.Operations.Items.Single().Status);
    }

    [Fact]
    public async Task Orchestrator_DigestChangedBeforeWorker_BlocksBeforeAdapterMutation()
    {
        Harness context = new();
        InstallSolutionResult started = await context.Orchestrator.BeginInstallAsync(
            context.Request,
            TestContext.Current.CancellationToken);
        context.Digest.Value = new string('9', 64);

        SolutionPackageException failure = await Assert.ThrowsAsync<SolutionPackageException>(() =>
            context.Orchestrator.RunOnceAsync(
                started.Operation.Id,
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken));

        Assert.Equal("solutions.package.axis_openapi_mismatch", failure.ProblemCode);
        Assert.Equal(0, context.Adapter.ApplyCalls);
        Assert.Equal(InstallationOperationStatus.Blocked, context.Operations.Items.Single().Status);
        Assert.Equal(ProvisioningStatus.Failed, context.Installations.Items.Single().ProvisioningStatus);
        Assert.Contains(
            context.Audit.Events,
            value => value.EventType == "solutions.install.blocked"
                && value.ProblemCode == "solutions.package.axis_openapi_mismatch");
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
        public MemoryVersions Versions { get; } = new(); public MemoryInstallations Installations { get; } = new(); public MemoryOperations Operations { get; } = new(); public Keys Keys { get; } = new(); public Digest Digest { get; } = new(new string('b', 64)); public Authority Authority { get; } = new(); public Audit Audit { get; } = new(); public UnitOfWork UnitOfWork { get; } = new(); public Adapter Adapter { get; }
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
            Orchestrator = new(new SolutionPackageVerifier(Keys), Versions, Installations, Operations, Keys, Digest, Authority, Audit, UnitOfWork, Adapters, new Clock(Now));
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
        public Task<SolutionInstallation?> FindBySolutionKeyAsync(Guid workspaceId, string solutionKey, CancellationToken cancellationToken = default) => Task.FromResult(Items.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.SolutionKey == solutionKey));
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
    private sealed class Keys : ITrustedPublisherKeyReader { public bool Active { get; set; } = true; public string PublicKeyPem { get; set; } = string.Empty; public Task<TrustedPublisherSnapshot?> FindAsync(string publisherId, string keyId, CancellationToken cancellationToken = default) => Task.FromResult<TrustedPublisherSnapshot?>(new(publisherId, keyId, PublicKeyPem, Active, !Active, 1)); }
    private sealed class Digest(string value) : ICurrentAxisOpenApiDigestProvider { public string Value { get; set; } = value; public string CurrentSha256 => Value; }
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
    private sealed class UnitOfWork : ISolutionsUnitOfWork
    {
        public int Saves;
        public Func<Exception?>? OnNextSave;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            Func<Exception?>? callback = OnNextSave;
            OnNextSave = null;
            Exception? failure = callback?.Invoke();
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }
    private sealed class Adapter(string componentType, bool fail) : ISolutionComponentAdapter
    {
        public string ComponentType => componentType; public int ApplyCalls; public int PreflightCalls; public bool ReadbackConfirmed; public bool RetryableFailure; public SolutionApplyReceipt? LastReceipt;
        public Task PreflightAsync(Guid workspaceId, SolutionAdapterPreflight component, CancellationToken cancellationToken = default) { PreflightCalls++; return fail ? Task.FromException(new SolutionAdapterException("preflight", false)) : Task.CompletedTask; }
        public Task<SolutionAdapterReadback> ReadBackAsync(Guid workspaceId, SolutionAdapterPreflight component, SolutionApplyReceipt receipt, CancellationToken cancellationToken = default) { LastReceipt = receipt; return Task.FromResult(new SolutionAdapterReadback(ReadbackConfirmed || ApplyCalls > 0 && !RetryableFailure, false)); }
        public Task ApplyAsync(Guid workspaceId, SolutionAdapterPreflight component, SolutionApplyReceipt receipt, CancellationToken cancellationToken = default) { ApplyCalls++; return RetryableFailure ? Task.FromException(new SolutionAdapterException("response_lost", true)) : Task.CompletedTask; }
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private static byte[] CreateSignedEnvelope(ECDsa key, string digest)
    {
        byte[] component = "{\"schemaVersion\":1}"u8.ToArray();
        string componentHash = Convert.ToHexString(SHA256.HashData(component)).ToLowerInvariant();
        string componentContent = Base64Url(component);
        byte[] payload = Encoding.UTF8.GetBytes(
            "{\"schemaVersion\":1,\"solutionKey\":\"publish_race\",\"solutionVersion\":\"1.0.0\",\"axisOpenApiSha256\":\"" + digest +
            "\",\"publisher\":{\"publisherId\":\"axis\",\"publisherKeyId\":\"release_key\"},\"provenance\":{\"sourceRevision\":\"" + new string('c', 40) +
            "\",\"buildId\":\"publish-race\",\"builtAt\":\"2026-08-07T00:00:00Z\",\"sourceUri\":\"https://example.test/publish-race\"},\"components\":[{\"type\":\"authorization.policy.v1\",\"key\":\"policy\",\"sha256\":\"" + componentHash +
            "\",\"content\":\"" + componentContent + "\",\"dependsOn\":[]}]}" );
        byte[] signature = key.SignData(
            SolutionPackageVerifier.CreatePae(SolutionPackageVerifier.PayloadType, payload),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Encoding.UTF8.GetBytes(
            "{\"payloadType\":\"" + SolutionPackageVerifier.PayloadType + "\",\"payload\":\"" +
            Base64Url(payload) + "\",\"signatures\":[{\"keyid\":\"release_key\",\"sig\":\"" +
            Base64Url(signature) + "\"}]}" );
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
