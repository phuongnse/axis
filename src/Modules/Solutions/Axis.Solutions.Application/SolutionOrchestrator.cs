using Axis.Audit.Contracts;
using Axis.Solutions.Contracts;
using Axis.Solutions.Domain;

namespace Axis.Solutions.Application;

public sealed class SolutionOrchestrator(
    SolutionPackageVerifier verifier,
    ISolutionVersionRepository versions,
    ISolutionInstallationRepository installations,
    ISolutionOperationRepository operations,
    ITrustedPublisherKeyReader trustedKeys,
    ICurrentAxisOpenApiDigestProvider currentAxisOpenApiDigest,
    ISolutionAuthority authority,
    ISolutionsAuditOutbox audit,
    ISolutionsUnitOfWork uow,
    IEnumerable<ISolutionComponentAdapter> adapters,
    TimeProvider clock)
{
    private readonly IReadOnlyDictionary<string, ISolutionComponentAdapter> _adapters = adapters.ToDictionary(x => x.ComponentType, StringComparer.Ordinal);

    public async Task<PublishSolutionResult> PublishAsync(PublishSolutionRequest request, CancellationToken cancellationToken = default)
    {
        await DemandAsync(request.Actor, request.Actor.WorkspaceId, SolutionAuthorityAction.Publish, request.RequestedAt, cancellationToken);
        VerifiedSolutionPackage package;
        try { package = await verifier.VerifyAsync(request.Envelope, RequireCurrentAxisOpenApiDigest(), cancellationToken); }
        catch (SolutionPackageException exception)
        {
            await AuditDeniedAsync(null, null, null, null, exception.ProblemCode, request.RequestedAt, cancellationToken, request.Actor);
            throw;
        }
        try
        {
            await PreflightComponentsAsync(request.Actor.WorkspaceId, package.Components, cancellationToken);
        }
        catch (Exception exception) when (exception is SolutionPackageException or SolutionAdapterException)
        {
            string problemCode = exception switch
            {
                SolutionPackageException packageException => packageException.ProblemCode,
                SolutionAdapterException adapterException => adapterException.ProblemCode,
                _ => "solutions.package.component_invalid",
            };
            await AuditDeniedAsync(null, null, null, null, problemCode, request.RequestedAt, cancellationToken, request.Actor);
            throw;
        }
        SolutionVersion? existing = await versions.FindByIdentityAsync(package.SolutionKey, package.SolutionVersion, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.PackageSha256, package.PackageSha256, StringComparison.Ordinal))
            {
                await AuditDeniedAsync(null, existing.Id, null, null, "solutions.version.conflict", request.RequestedAt, cancellationToken, request.Actor);
                throw new SolutionPackageException("solutions.version.conflict");
            }
            await PersistAuditAsync(
                NewAudit("solutions.version.publish_retried", null, existing.Id, null, null, "canonical_retry", null, request.RequestedAt, actor: request.Actor),
                cancellationToken);
            return new PublishSolutionResult(
                ToDto(
                    existing,
                    SolutionTrustStatus.Trusted,
                    await versions.GetComponentsAsync(existing.Id, cancellationToken)),
                true);
        }

        SolutionVersion version = SolutionVersion.Create(package.SolutionKey, package.SolutionVersion, package.PackageSha256,
            package.EnvelopeBytes, package.AxisOpenApiSha256, package.PublisherId, package.PublisherKeyId,
            package.Provenance.SourceRevision, package.Provenance.BuildId, package.Provenance.BuiltAt,
            package.Provenance.SourceUri, request.RequestedAt);
        await versions.AddAsync(version, package.Components, cancellationToken);
        SolutionAuditEvent published = NewAudit("solutions.version.published", null, version.Id, null, null, "succeeded", null, request.RequestedAt, actor: request.Actor);
        await audit.EnqueueAsync(published, cancellationToken);
        try
        {
            await uow.SaveChangesAsync(cancellationToken);
        }
        catch (SolutionPersistenceException exception) when (
            exception.ProblemCode == "solutions.persistence.version_identity_conflict")
        {
            await uow.RollbackAsync(cancellationToken);
            return await ResolveConcurrentPublishAsync(package, request, cancellationToken);
        }
        await ConfirmAuditAsync(published.EventId, cancellationToken);
        return new PublishSolutionResult(
            ToDto(version, SolutionTrustStatus.Trusted, package.Components),
            false);
    }

    public async Task<InstallSolutionResult> BeginInstallAsync(InstallSolutionRequest request, CancellationToken cancellationToken = default)
    {
        await DemandAsync(request.Actor, request.WorkspaceId, SolutionAuthorityAction.Install, request.RequestedAt, cancellationToken);
        SolutionInstallationOperation? retry = await operations.FindByIdempotencyAsync(request.WorkspaceId, request.IdempotencyKey, cancellationToken);
        if (retry is not null)
        {
            if (!string.Equals(retry.RequestHash, request.RequestHash, StringComparison.Ordinal))
            {
                await AuditDeniedAsync(request.WorkspaceId, null, retry.InstallationId, retry.Id, "solutions.install.idempotency_conflict", request.RequestedAt, cancellationToken, request.Actor);
                throw new SolutionPackageException("solutions.install.idempotency_conflict");
            }
            await PersistAuditAsync(
                NewAudit("solutions.install.retried", request.WorkspaceId, null, retry.InstallationId, retry.Id, "canonical_retry", null, request.RequestedAt, actor: request.Actor),
                cancellationToken);
            return new InstallSolutionResult(ToDto(retry), true);
        }

        SolutionVersion version;
        try { version = await RequireInstallableVersion(request.SolutionVersionId, cancellationToken); }
        catch (SolutionPackageException exception)
        {
            await AuditDeniedAsync(request.WorkspaceId, request.SolutionVersionId, null, null, exception.ProblemCode, request.RequestedAt, cancellationToken, request.Actor);
            throw;
        }
        SolutionInstallation? existing = await installations.FindBySolutionKeyAsync(request.WorkspaceId, version.SolutionKey, cancellationToken);
        if (existing is not null)
        {
            await AuditDeniedAsync(request.WorkspaceId, version.Id, existing.Id, null, "solutions.install.already_exists", request.RequestedAt, cancellationToken, request.Actor);
            throw new SolutionPackageException("solutions.install.already_exists");
        }
        IReadOnlyList<VerifiedSolutionComponent> components = await versions.GetComponentsAsync(version.Id, cancellationToken);
        IReadOnlyList<SolutionComponentPlan> plan;
        try
        {
            plan = await PreflightComponentsAsync(request.WorkspaceId, components, cancellationToken);
        }
        catch (Exception exception) when (exception is SolutionPackageException or SolutionAdapterException)
        {
            string problemCode = exception switch
            {
                SolutionPackageException packageException => packageException.ProblemCode,
                SolutionAdapterException adapterException => adapterException.ProblemCode,
                _ => "solutions.install.preflight_failed",
            };
            await AuditDeniedAsync(request.WorkspaceId, version.Id, null, null, problemCode, request.RequestedAt, cancellationToken, request.Actor);
            throw;
        }

        SolutionInstallation installation = SolutionInstallation.Create(
            request.WorkspaceId,
            version.SolutionKey,
            version.Id,
            request.RequestedAt);
        SolutionInstallationOperation operation = SolutionInstallationOperation.Create(
            request.WorkspaceId,
            request.Actor.SubjectId,
            request.Actor.SubjectKind,
            request.Actor.CorrelationId,
            installation.Id,
            request.IdempotencyKey,
            request.RequestHash,
            plan,
            request.RequestedAt);
        await installations.AddAsync(installation, cancellationToken);
        await operations.AddAsync(operation, cancellationToken);
        SolutionAuditEvent requested = NewAudit("solutions.install.requested", request.WorkspaceId, version.Id, installation.Id, operation.Id, "accepted", null, request.RequestedAt, actor: request.Actor);
        await audit.EnqueueAsync(requested, cancellationToken);
        try
        {
            await uow.SaveChangesAsync(cancellationToken);
        }
        catch (SolutionPersistenceException exception) when (exception.ProblemCode is
            "solutions.persistence.installation_solution_conflict" or
            "solutions.persistence.operation_idempotency_conflict")
        {
            await uow.RollbackAsync(cancellationToken);
            return await ResolveConcurrentInstallAsync(request, version, cancellationToken);
        }
        await ConfirmAuditAsync(requested.EventId, cancellationToken);
        return new InstallSolutionResult(ToDto(operation), false);
    }

    public async Task<SolutionOperationStatusDto> ResumeAsync(SolutionActor actor, Guid operationId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        SolutionInstallationOperation operation = await RequireOperation(operationId, cancellationToken);
        await DemandAsync(actor, operation.WorkspaceId, SolutionAuthorityAction.Resume, now, cancellationToken);
        if (operation.Status != InstallationOperationStatus.Failed)
        {
            await AuditDeniedAsync(
                operation.WorkspaceId,
                null,
                operation.InstallationId,
                operation.Id,
                "solutions.install.operation_not_resumable",
                now,
                cancellationToken,
                actor);
            throw new SolutionPackageException("solutions.install.operation_not_resumable");
        }
        try { await RequireTrustedForOperation(operation, cancellationToken); }
        catch (SolutionPackageException exception)
        {
            if (exception.ProblemCode == "solutions.package.publisher_untrusted")
            {
                SolutionInstallation installation = await RequireInstallation(operation.InstallationId, cancellationToken);
                await MarkUntrustedAsync(installation, operation, now, cancellationToken, actor);
            }
            else
            {
                await AuditDeniedAsync(null, null, operation.InstallationId, operation.Id, exception.ProblemCode, now, cancellationToken, actor);
            }
            throw;
        }
        operation.Resume(now);
        SolutionAuditEvent resumed = NewAudit("solutions.install.resumed", null, null, operation.InstallationId, operation.Id, "accepted", null, now, actor: actor);
        await audit.EnqueueAsync(resumed, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        await ConfirmAuditAsync(resumed.EventId, cancellationToken);
        return ToDto(operation);
    }

    public async Task<SolutionOperationStatusDto> GetOperationStatusAsync(SolutionActor actor, Guid operationId, CancellationToken cancellationToken = default)
    {
        SolutionInstallationOperation operation = await RequireOperation(operationId, cancellationToken);
        await DemandAsync(actor, operation.WorkspaceId, SolutionAuthorityAction.Read, clock.GetUtcNow(), cancellationToken);
        return ToDto(operation);
    }

    public async Task<IReadOnlyList<SolutionInstallationStatusDto>> ListInstallationStatusAsync(SolutionActor actor, CancellationToken cancellationToken = default)
    {
        await DemandAsync(actor, actor.WorkspaceId, SolutionAuthorityAction.Read, clock.GetUtcNow(), cancellationToken);
        IReadOnlyList<SolutionInstallation> values = await installations.ListByWorkspaceAsync(actor.WorkspaceId, cancellationToken);
        List<SolutionInstallationStatusDto> result = [];
        foreach (SolutionInstallation installation in values)
        {
            SolutionInstallationOperation? operation = (await operations.ListByInstallationAsync(installation.Id, cancellationToken)).OrderByDescending(x => x.UpdatedAt).FirstOrDefault();
            IReadOnlyList<SolutionComponentStatusDto> components = operation?.Steps.OrderBy(x => x.Order).Select(x => new SolutionComponentStatusDto(x.Type, x.Key, x.Sha256, (SolutionStepStatus)x.Status, x.ProblemCode)).ToArray() ?? [];
            result.Add(new SolutionInstallationStatusDto(
                installation.Id,
                installation.WorkspaceId,
                installation.SolutionVersionId,
                operation?.Id,
                operation is null ? null : (SolutionOperationStatus)operation.Status,
                (SolutionProvisioningStatus)installation.ProvisioningStatus,
                (SolutionComplianceStatus)installation.ComplianceStatus,
                components,
                installation.UpdatedAt));
        }
        return result;
    }

    public async Task<IReadOnlyList<SolutionVersionSummaryDto>> ListVersionStatusAsync(
        SolutionActor actor,
        CancellationToken cancellationToken = default)
    {
        await DemandAsync(actor, actor.WorkspaceId, SolutionAuthorityAction.Read, clock.GetUtcNow(), cancellationToken);
        IReadOnlyList<SolutionVersion> values = await versions.ListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, IReadOnlyList<VerifiedSolutionComponent>> components =
            await versions.GetComponentsAsync(values.Select(value => value.Id).ToArray(), cancellationToken);
        List<SolutionVersionSummaryDto> result = [];
        foreach (SolutionVersion value in values)
            result.Add(ToDto(
                value,
                await TrustStatusAsync(value, cancellationToken),
                components[value.Id]));
        return result;
    }

    public async Task<SolutionVersionSummaryDto> GetVersionStatusAsync(
        SolutionActor actor,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        await DemandAsync(actor, actor.WorkspaceId, SolutionAuthorityAction.Read, clock.GetUtcNow(), cancellationToken);
        SolutionVersion value = await versions.FindByIdAsync(versionId, cancellationToken)
            ?? throw new SolutionPackageException("solutions.version.not_found");
        return ToDto(
            value,
            await TrustStatusAsync(value, cancellationToken),
            await versions.GetComponentsAsync(value.Id, cancellationToken));
    }

    public async Task<SolutionOperationStatusDto> RunOnceAsync(Guid operationId, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.GetUtcNow();
        SolutionInstallationOperation operation = await RequireOperation(operationId, cancellationToken);
        SolutionInstallation installation = await RequireInstallation(operation.InstallationId, cancellationToken);
        SolutionVersion version;
        try { version = await RequireInstallableVersion(installation.SolutionVersionId, cancellationToken); }
        catch (SolutionPackageException exception)
        {
            if (exception.ProblemCode == "solutions.package.publisher_untrusted")
                await MarkUntrustedAsync(installation, operation, now, cancellationToken);
            else if (exception.ProblemCode == "solutions.package.axis_openapi_mismatch")
                await MarkIncompatibleAsync(installation, operation, now, cancellationToken);
            else
                await AuditDeniedAsync(installation.WorkspaceId, installation.SolutionVersionId, installation.Id, operation.Id, exception.ProblemCode, now, cancellationToken, operation: operation);
            throw;
        }
        long epoch = operation.AcquireLease(now, leaseDuration);
        await uow.SaveChangesAsync(cancellationToken);
        SolutionAuditEvent? stepAudit = null;
        await uow.BeginAsync(cancellationToken);
        try
        {
            try
            {
                await uow.AcquirePublisherFenceAsync(version.PublisherId, cancellationToken);
                try { version = await RequireInstallableVersion(installation.SolutionVersionId, cancellationToken); }
                catch (SolutionPackageException exception)
                {
                    if (exception.ProblemCode == "solutions.package.publisher_untrusted")
                        await MarkUntrustedAsync(installation, operation, clock.GetUtcNow(), cancellationToken);
                    else if (exception.ProblemCode == "solutions.package.axis_openapi_mismatch")
                        await MarkIncompatibleAsync(installation, operation, clock.GetUtcNow(), cancellationToken);
                    else
                        await AuditDeniedAsync(installation.WorkspaceId, installation.SolutionVersionId, installation.Id, operation.Id, exception.ProblemCode, clock.GetUtcNow(), cancellationToken, operation: operation);
                    await uow.CommitAsync(cancellationToken);
                    throw;
                }

                SolutionInstallationStep step = operation.ClaimNext(epoch, clock.GetUtcNow());
                IReadOnlyList<VerifiedSolutionComponent> components = await versions.GetComponentsAsync(version.Id, cancellationToken);
                VerifiedSolutionComponent component = components.Single(x => x.Type == step.Type && x.Key == step.Key);
                SolutionAdapterPreflight adapterComponent = ToAdapterComponent(components, new SolutionComponentPlan(step.Type, step.Key, step.Sha256, component.DependsOn.Select(x => new SolutionComponentIdentity(x.Type, x.Key)).ToArray()));
                ISolutionComponentAdapter adapter = _adapters[step.Type];
                SolutionApplyReceipt receipt = new(
                    operation.Id,
                    step.Id,
                    version.Id,
                    operation.ActorSubjectId,
                    operation.ActorSubjectKind,
                    operation.ActorCorrelationId,
                    version.Version,
                    step.Sha256,
                    epoch);
                SolutionAdapterReadback readback = await adapter.ReadBackAsync(
                    installation.WorkspaceId,
                    adapterComponent,
                    receipt,
                    cancellationToken);
                if (readback.IsMismatch)
                    throw new SolutionAdapterException(readback.ProblemCode ?? "solutions.install.readback_mismatch", retryable: false);
                if (!readback.IsConfirmed)
                {
                    await adapter.ApplyAsync(installation.WorkspaceId, adapterComponent, receipt, cancellationToken);
                    readback = await adapter.ReadBackAsync(
                        installation.WorkspaceId,
                        adapterComponent,
                        receipt,
                        cancellationToken);
                    if (readback.IsMismatch)
                        throw new SolutionAdapterException(readback.ProblemCode ?? "solutions.install.readback_mismatch", retryable: false);
                    if (!readback.IsConfirmed)
                        throw new SolutionAdapterException("solutions.install.readback_unconfirmed", retryable: true);
                }
                operation.Confirm(step.Id, epoch, clock.GetUtcNow());
                if (operation.Status == InstallationOperationStatus.Succeeded)
                    installation.MarkInstalled(clock.GetUtcNow());
                stepAudit = NewAudit("solutions.install.step", installation.WorkspaceId, version.Id, installation.Id, operation.Id, "succeeded", null, clock.GetUtcNow(), operation: operation);
                await audit.EnqueueAsync(stepAudit, cancellationToken);
            }
            catch (SolutionAdapterException exception) when (exception.Retryable)
            {
                SolutionInstallationStep step = operation.Steps.Single(x => x.Status == InstallationStepStatus.Applying);
                operation.RecordRetryableFailure(step.Id, epoch, exception.ProblemCode, clock.GetUtcNow());
                stepAudit = NewAudit("solutions.install.step", installation.WorkspaceId, version.Id, installation.Id, operation.Id, "retryable_failure", exception.ProblemCode, clock.GetUtcNow(), operation: operation);
                await audit.EnqueueAsync(stepAudit, cancellationToken);
            }
            catch (SolutionAdapterException exception)
            {
                SolutionInstallationStep step = operation.Steps.Single(x => x.Status == InstallationStepStatus.Applying);
                operation.Block(step.Id, epoch, exception.ProblemCode, clock.GetUtcNow());
                installation.MarkFailed(clock.GetUtcNow());
                stepAudit = NewAudit("solutions.install.step", installation.WorkspaceId, version.Id, installation.Id, operation.Id, "blocked", exception.ProblemCode, clock.GetUtcNow(), operation: operation);
                await audit.EnqueueAsync(stepAudit, cancellationToken);
            }
            await uow.SaveChangesAsync(cancellationToken);
            await uow.CommitAsync(cancellationToken);
        }
        catch
        {
            await uow.RollbackAsync(cancellationToken);
            throw;
        }
        if (stepAudit is not null)
            await ConfirmAuditAsync(stepAudit.EventId, cancellationToken);
        return ToDto(operation);
    }

    public async Task MarkRevokedNoncompliantAsync(string publisherId, string keyId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SolutionInstallation> affected = await installations.ListByPublisherKeyAsync(publisherId, keyId, cancellationToken);
        List<Guid> auditEventIds = [];
        foreach (SolutionInstallation installation in affected)
        {
            installation.MarkNoncompliant(now);
            if (installation.ProvisioningStatus == ProvisioningStatus.Installing)
            {
                installation.MarkFailed(now);
                IReadOnlyList<SolutionInstallationOperation> installationOperations =
                    await operations.ListTrackedByInstallationAsync(installation.Id, cancellationToken);
                foreach (SolutionInstallationOperation operation in installationOperations)
                    operation.BlockBeforeNextMutation("solutions.package.publisher_untrusted", now);
            }
            SolutionAuditEvent auditEvent = NewAudit("solutions.installation.noncompliant", installation.WorkspaceId, null, installation.Id, null, "revoked", "solutions.package.publisher_untrusted", now);
            auditEventIds.Add(auditEvent.EventId);
            await audit.EnqueueAsync(auditEvent, cancellationToken);
        }
        await uow.SaveChangesAsync(cancellationToken);
        foreach (Guid eventId in auditEventIds)
            await ConfirmAuditAsync(eventId, cancellationToken);
    }

    private async Task<PublishSolutionResult> ResolveConcurrentPublishAsync(
        VerifiedSolutionPackage package,
        PublishSolutionRequest request,
        CancellationToken cancellationToken)
    {
        SolutionVersion existing = await versions.FindByIdentityAsync(
            package.SolutionKey,
            package.SolutionVersion,
            cancellationToken) ?? throw new SolutionPackageException("solutions.version.conflict");
        if (!string.Equals(existing.PackageSha256, package.PackageSha256, StringComparison.Ordinal))
        {
            await AuditDeniedAsync(
                null,
                existing.Id,
                null,
                null,
                "solutions.version.conflict",
                request.RequestedAt,
                cancellationToken,
                request.Actor);
            throw new SolutionPackageException("solutions.version.conflict");
        }
        await PersistAuditAsync(
            NewAudit(
                "solutions.version.publish_retried",
                null,
                existing.Id,
                null,
                null,
                "canonical_retry",
                null,
                request.RequestedAt,
                actor: request.Actor),
            cancellationToken);
        return new PublishSolutionResult(
            ToDto(
                existing,
                SolutionTrustStatus.Trusted,
                await versions.GetComponentsAsync(existing.Id, cancellationToken)),
            true);
    }

    private async Task<InstallSolutionResult> ResolveConcurrentInstallAsync(
        InstallSolutionRequest request,
        SolutionVersion version,
        CancellationToken cancellationToken)
    {
        SolutionInstallationOperation? retry = await operations.FindByIdempotencyAsync(
            request.WorkspaceId,
            request.IdempotencyKey,
            cancellationToken);
        if (retry is not null)
        {
            if (!string.Equals(retry.RequestHash, request.RequestHash, StringComparison.Ordinal))
            {
                await AuditDeniedAsync(
                    request.WorkspaceId,
                    version.Id,
                    retry.InstallationId,
                    retry.Id,
                    "solutions.install.idempotency_conflict",
                    request.RequestedAt,
                    cancellationToken,
                    request.Actor);
                throw new SolutionPackageException("solutions.install.idempotency_conflict");
            }
            await PersistAuditAsync(
                NewAudit(
                    "solutions.install.retried",
                    request.WorkspaceId,
                    version.Id,
                    retry.InstallationId,
                    retry.Id,
                    "canonical_retry",
                    null,
                    request.RequestedAt,
                    actor: request.Actor),
                cancellationToken);
            return new InstallSolutionResult(ToDto(retry), true);
        }

        SolutionInstallation? existing = await installations.FindBySolutionKeyAsync(
            request.WorkspaceId,
            version.SolutionKey,
            cancellationToken);
        await AuditDeniedAsync(
            request.WorkspaceId,
            version.Id,
            existing?.Id,
            null,
            "solutions.install.already_exists",
            request.RequestedAt,
            cancellationToken,
            request.Actor);
        throw new SolutionPackageException("solutions.install.already_exists");
    }

    private async Task MarkIncompatibleAsync(
        SolutionInstallation installation,
        SolutionInstallationOperation operation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (installation.ProvisioningStatus == ProvisioningStatus.Installing)
            installation.MarkFailed(now);
        operation.BlockBeforeNextMutation("solutions.package.axis_openapi_mismatch", now);
        await PersistAuditAsync(
            NewAudit(
                "solutions.install.blocked",
                installation.WorkspaceId,
                installation.SolutionVersionId,
                installation.Id,
                operation.Id,
                "incompatible",
                "solutions.package.axis_openapi_mismatch",
                now,
                operation: operation),
            cancellationToken);
    }

    private async Task MarkUntrustedAsync(
        SolutionInstallation installation,
        SolutionInstallationOperation operation,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        SolutionActor? actor = null)
    {
        installation.MarkNoncompliant(now);
        if (installation.ProvisioningStatus == ProvisioningStatus.Installing)
        {
            installation.MarkFailed(now);
            operation.BlockBeforeNextMutation("solutions.package.publisher_untrusted", now);
        }
        SolutionAuditEvent auditEvent = NewAudit(
            "solutions.installation.noncompliant",
            installation.WorkspaceId,
            installation.SolutionVersionId,
            installation.Id,
            operation.Id,
            "revoked",
            "solutions.package.publisher_untrusted",
            now,
            actor,
            actor is null ? operation : null);
        await PersistAuditAsync(auditEvent, cancellationToken);
    }

    private async Task<SolutionVersion> RequireInstallableVersion(Guid versionId, CancellationToken cancellationToken)
    {
        SolutionVersion version = await versions.FindByIdAsync(versionId, cancellationToken) ?? throw new SolutionPackageException("solutions.version.not_found");
        if (!string.Equals(
                version.AxisOpenApiSha256,
                RequireCurrentAxisOpenApiDigest(),
                StringComparison.Ordinal))
        {
            throw new SolutionPackageException("solutions.package.axis_openapi_mismatch");
        }
        TrustedPublisherSnapshot? key = await trustedKeys.FindAsync(version.PublisherId, version.PublisherKeyId, cancellationToken);
        if (key is null || !key.IsActive || key.IsTombstone)
            throw new SolutionPackageException("solutions.package.publisher_untrusted");
        return version;
    }

    private string RequireCurrentAxisOpenApiDigest()
    {
        string? value = currentAxisOpenApiDigest.CurrentSha256;
        if (value is not { Length: 64 }
            || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new SolutionPackageException("solutions.configuration.unavailable");
        }
        return value;
    }

    private async Task<SolutionTrustStatus> TrustStatusAsync(
        SolutionVersion version,
        CancellationToken cancellationToken)
    {
        TrustedPublisherSnapshot? key = await trustedKeys.FindAsync(
            version.PublisherId,
            version.PublisherKeyId,
            cancellationToken);
        if (key is null)
            return SolutionTrustStatus.Unknown;
        return key.IsActive && !key.IsTombstone
            ? SolutionTrustStatus.Trusted
            : SolutionTrustStatus.Revoked;
    }

    private async Task RequireTrustedForOperation(SolutionInstallationOperation operation, CancellationToken cancellationToken)
    {
        SolutionInstallation installation = await RequireInstallation(operation.InstallationId, cancellationToken);
        await RequireInstallableVersion(installation.SolutionVersionId, cancellationToken);
    }

    private async Task<SolutionInstallationOperation> RequireOperation(Guid id, CancellationToken cancellationToken) =>
        await operations.FindByIdAsync(id, cancellationToken) ?? throw new SolutionPackageException("solutions.operation.not_found");
    private async Task<SolutionInstallation> RequireInstallation(Guid id, CancellationToken cancellationToken) =>
        await installations.FindByIdAsync(id, cancellationToken) ?? throw new SolutionPackageException("solutions.installation.not_found");

    private IReadOnlyList<SolutionComponentPlan> BuildPlan(IReadOnlyList<VerifiedSolutionComponent> components)
    {
        if (components.Count == 0 || components.Any(x => !_adapters.ContainsKey(x.Type)))
            throw new SolutionPackageException("solutions.install.adapter_unavailable");
        return TopologicalPlan(components);
    }

    private async Task<IReadOnlyList<SolutionComponentPlan>> PreflightComponentsAsync(
        Guid workspaceId,
        IReadOnlyList<VerifiedSolutionComponent> components,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SolutionComponentPlan> plan = BuildPlan(components);
        foreach (SolutionComponentPlan entry in plan)
        {
            await _adapters[entry.Type].PreflightAsync(
                workspaceId,
                ToAdapterComponent(components, entry),
                cancellationToken);
        }
        return plan;
    }

    private static IReadOnlyList<SolutionComponentPlan> TopologicalPlan(
        IReadOnlyList<VerifiedSolutionComponent> components)
    {
        List<SolutionComponentPlan> result = [];
        HashSet<(string, string)> remaining = components.Select(x => (x.Type, x.Key)).ToHashSet();
        while (remaining.Count > 0)
        {
            VerifiedSolutionComponent? next = components.FirstOrDefault(x => remaining.Contains((x.Type, x.Key)) && x.DependsOn.All(d => !remaining.Contains((d.Type, d.Key))));
            if (next is null)
                throw new SolutionPackageException("solutions.package.dependencies_invalid");
            remaining.Remove((next.Type, next.Key));
            result.Add(new SolutionComponentPlan(next.Type, next.Key, next.Sha256, next.DependsOn.Select(x => new SolutionComponentIdentity(x.Type, x.Key)).ToArray()));
        }
        return result;
    }

    private static SolutionAdapterPreflight ToAdapterComponent(IReadOnlyList<VerifiedSolutionComponent> components, SolutionComponentPlan plan)
    {
        VerifiedSolutionComponent component = components.Single(x => x.Type == plan.Type && x.Key == plan.Key);
        return new SolutionAdapterPreflight(component.Type, component.Key, component.Content, component.DependsOn);
    }

    private static SolutionAuditEvent NewAudit(
        string type,
        Guid? workspaceId,
        Guid? versionId,
        Guid? installationId,
        Guid? operationId,
        string outcome,
        string? problem,
        DateTimeOffset at,
        SolutionActor? actor = null,
        SolutionInstallationOperation? operation = null)
    {
        Guid eventId = Guid.NewGuid();
        bool hasValidActor = actor is not null
            && actor.SubjectId != Guid.Empty
            && actor.WorkspaceId != Guid.Empty
            && Enum.IsDefined(actor.SubjectKind)
            && !string.IsNullOrWhiteSpace(actor.CorrelationId)
            && actor.CorrelationId.Trim().Length <= AuditEventV1Validator.MaximumCorrelationIdLength;
        AuditActorKindV1 actorKind = hasValidActor
            ? actor!.SubjectKind == SolutionSubjectKind.Human
                ? AuditActorKindV1.Human
                : AuditActorKindV1.ServiceIdentity
            : AuditActorKindV1.System;
        Guid? actorId = hasValidActor ? actor!.SubjectId : null;
        Guid? subjectId = hasValidActor ? actor!.SubjectId : operation?.ActorSubjectId;
        string correlationId = hasValidActor
            ? actor!.CorrelationId.Trim()
            : operation?.ActorCorrelationId ?? $"solutions-{eventId:N}";
        return new(
            eventId,
            actorKind,
            actorId,
            subjectId,
            correlationId,
            operation?.ActorSubjectKind,
            type,
            workspaceId ?? (hasValidActor ? actor!.WorkspaceId : operation?.WorkspaceId),
            versionId,
            installationId,
            operationId,
            outcome,
            problem,
            at);
    }

    private async Task AuditDeniedAsync(
        Guid? workspaceId,
        Guid? versionId,
        Guid? installationId,
        Guid? operationId,
        string problemCode,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        SolutionActor? actor = null,
        SolutionInstallationOperation? operation = null)
    {
        await PersistAuditAsync(
            NewAudit("solutions.denied", workspaceId, versionId, installationId, operationId, "denied", problemCode, now, actor, operation),
            cancellationToken);
    }

    private async Task PersistAuditAsync(SolutionAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        await audit.EnqueueAsync(auditEvent, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        await ConfirmAuditAsync(auditEvent.EventId, cancellationToken);
    }

    private async Task ConfirmAuditAsync(Guid eventId, CancellationToken cancellationToken)
    {
        if (!await audit.ExistsAsync(eventId, cancellationToken))
            throw new SolutionPackageException("solutions.audit.readback_failed");
    }

    private async Task DemandAsync(SolutionActor actor, Guid workspaceId, SolutionAuthorityAction action, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            actor.Validate();
            if (actor.WorkspaceId != workspaceId)
                throw new SolutionPackageException("solutions.authorization.workspace_mismatch");
            await authority.DemandAsync(actor, workspaceId, action, cancellationToken);
        }
        catch (SolutionPackageException exception)
        {
            await AuditDeniedAsync(workspaceId, null, null, null, exception.ProblemCode, now, cancellationToken, actor);
            throw;
        }
    }

    public static SolutionVersionSummaryDto ToDto(
        SolutionVersion value,
        SolutionTrustStatus trust,
        IReadOnlyList<VerifiedSolutionComponent> components) =>
        new(
            value.Id,
            value.SolutionKey,
            value.Version,
            value.PackageSha256,
            value.AxisOpenApiSha256,
            value.PublisherId,
            value.PublisherKeyId,
            trust,
            value.SourceRevision,
            value.BuildId,
            value.BuiltAt,
            new Uri(value.SourceUri),
            value.PublishedAt,
            TopologicalPlan(components).Select(component => new SolutionComponentPlanDto(
                component.Type,
                component.Key,
                component.Sha256,
                component.DependsOn.Select(dependency =>
                    new SolutionComponentIdentityDto(dependency.Type, dependency.Key)).ToArray()))
            .ToArray());
    public static SolutionOperationStatusDto ToDto(SolutionInstallationOperation value) => new(value.Id, value.InstallationId, (SolutionOperationStatus)value.Status, value.LeaseEpoch, value.ProblemCode, value.Steps.OrderBy(x => x.Order).Select(x => new SolutionComponentStatusDto(x.Type, x.Key, x.Sha256, (SolutionStepStatus)x.Status, x.ProblemCode)).ToArray(), value.UpdatedAt);
}
