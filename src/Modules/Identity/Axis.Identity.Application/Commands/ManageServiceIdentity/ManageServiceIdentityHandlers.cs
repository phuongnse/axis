using System.Security.Cryptography;
using System.Text;
using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ManageServiceIdentity;

public sealed class CreateServiceIdentityHandler(
    IWorkspaceMembershipRepository memberships,
    IServiceIdentityRepository identities,
    IIdentityAuditOutbox audit,
    TimeProvider clock,
    IUnitOfWork uow,
    IServiceIdentityClientProjection projection)
    : ICommandHandler<CreateServiceIdentityCommand, ServiceIdentityDto>
{
    public async Task<Result<ServiceIdentityDto>> Handle(
        CreateServiceIdentityCommand command,
        CancellationToken ct)
    {
        if (!await IsAdministrator(memberships, command.WorkspaceId, command.ActorUserId, ct))
        {
            return await PersistDenied(
                command.ActorUserId,
                command.WorkspaceId,
                Guid.NewGuid(),
                "service_identity.create_denied",
                command.CorrelationId,
                audit,
                clock,
                uow,
                ct);
        }

        if (string.IsNullOrWhiteSpace(command.ClientId) || command.ClientId.Trim().Length > 100)
            return Conflict();

        string clientId = command.ClientId.Trim();
        ServiceIdentity? prior = await identities.GetByClientIdAsync(clientId, ct);
        if (prior is not null)
        {
            return prior.WorkspaceId == command.WorkspaceId
                ? await ReadCanonical(
                    prior,
                    Event(command.ActorUserId, prior, "service_identity.created", "succeeded", command.CorrelationId, clock),
                    identities,
                    audit,
                    ct)
                : Conflict();
        }

        ServiceIdentity identity = ServiceIdentity.Create(
            command.WorkspaceId,
            clientId,
            clock.GetUtcNow().UtcDateTime);
        await identities.AddAsync(identity, ct);
        if (!await TryStageProjectionAsync(projection, identity, uow, ct))
            return Conflict();

        AuditEventV1 auditEvent = Event(
            command.ActorUserId,
            identity,
            "service_identity.created",
            "succeeded",
            command.CorrelationId,
            clock);
        if (!await TryEnqueueAsync(audit, auditEvent, uow, ct))
            return AuditUnavailable();

        return await SaveAndRead(
            identity,
            auditEvent,
            identities,
            audit,
            uow,
            async () =>
            {
                ServiceIdentity? concurrent = await identities.GetByClientIdAsync(clientId, ct);
                return concurrent is not null && concurrent.WorkspaceId == command.WorkspaceId
                    ? await ReadCanonical(
                        concurrent,
                        Event(command.ActorUserId, concurrent, "service_identity.created", "succeeded", command.CorrelationId, clock),
                        identities,
                        audit,
                        ct)
                    : Conflict();
            },
            ct);
    }

    internal static bool IsAdmin(WorkspaceMembership? membership) =>
        membership is { Status: MembershipStatus.Active, Role: WorkspaceMembershipRole.Administrator };

    internal static async Task<bool> IsAdministrator(
        IWorkspaceMembershipRepository memberships,
        Guid workspaceId,
        Guid actorId,
        CancellationToken ct) =>
        IsAdmin(await memberships.GetActiveAsync(workspaceId, actorId, ct));

    internal static AuditEventV1 Event(
        Guid actor,
        ServiceIdentity identity,
        string action,
        string outcome,
        string correlation,
        TimeProvider clock,
        Guid? keyId = null)
    {
        Dictionary<string, string> metadata = new Dictionary<string, string>
        {
            ["workspaceId"] = identity.WorkspaceId.ToString(),
            ["serviceIdentityId"] = identity.Id.ToString(),
            ["status"] = identity.Status.ToString(),
        };
        if (keyId.HasValue)
            metadata["keyId"] = keyId.Value.ToString();

        return new AuditEventV1(
            LifecycleEventId(identity.Id, action, keyId),
            AuditActorKindV1.Human,
            actor,
            actor,
            identity.WorkspaceId,
            action,
            "ServiceIdentity",
            identity.Id,
            outcome,
            clock.GetUtcNow(),
            correlation.Trim(),
            metadata);
    }

    internal static async Task<Result<ServiceIdentityDto>> SaveAndRead(
        ServiceIdentity identity,
        AuditEventV1 expectedAudit,
        IServiceIdentityRepository identities,
        IIdentityAuditOutbox audit,
        IUnitOfWork uow,
        Func<Task<Result<ServiceIdentityDto>>> resolveConflict,
        CancellationToken ct)
    {
        int expectedRevision = identity.Revision;
        try
        {
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (Exception ex) when (ex is ConcurrencyException or UniqueConstraintException)
        {
            uow.ClearTracking();
            return await resolveConflict();
        }
        catch (Exception)
        {
            uow.ClearTracking();
            return AuditUnavailable();
        }

        ServiceIdentity? persisted;
        IdentityAuditOutboxEntry? persistedAudit;
        try
        {
            persisted = await identities.GetAsync(identity.WorkspaceId, identity.Id, ct);
            persistedAudit = await audit.GetAsync(expectedAudit.EventId, ct);
        }
        catch (Exception)
        {
            return ReadBackFailed();
        }

        return persisted is null
            || persisted.Revision != expectedRevision
            || !IsExpectedAudit(persistedAudit, expectedAudit, requireCorrelation: true)
            ? ReadBackFailed()
            : Result.Success(persisted.ToDto());
    }

    internal static async Task<Result<ServiceIdentityDto>> ReadCanonical(
        ServiceIdentity identity,
        AuditEventV1 expectedAudit,
        IServiceIdentityRepository identities,
        IIdentityAuditOutbox audit,
        CancellationToken ct)
    {
        try
        {
            ServiceIdentity? persisted = await identities.GetAsync(identity.WorkspaceId, identity.Id, ct);
            IdentityAuditOutboxEntry? persistedAudit = await audit.GetAsync(expectedAudit.EventId, ct);
            return persisted is null || !IsExpectedAudit(persistedAudit, expectedAudit, requireCorrelation: false)
                ? ReadBackFailed()
                : Result.Success(persisted.ToDto());
        }
        catch (Exception)
        {
            return ReadBackFailed();
        }
    }

    internal static Result<ServiceIdentityDto> Forbidden() =>
        Result.Failure<ServiceIdentityDto>(
            ErrorCodes.Forbidden,
            "Service identity access is unavailable.",
            "identity.serviceIdentity.forbidden");

    internal static Result<ServiceIdentityDto> Conflict() =>
        Result.Failure<ServiceIdentityDto>(
            ErrorCodes.Conflict,
            "Service identity lifecycle conflicts with current state.",
            "identity.serviceIdentity.conflict");

    internal static Result<ServiceIdentityDto> AuditUnavailable() =>
        Result.Failure<ServiceIdentityDto>(
            ErrorCodes.BusinessRule,
            "Required service identity audit work is unavailable.",
            "identity.serviceIdentity.auditUnavailable");

    private static Result<ServiceIdentityDto> ReadBackFailed() =>
        Result.Failure<ServiceIdentityDto>(
            ErrorCodes.BusinessRule,
            "Service identity write could not be confirmed.",
            "identity.serviceIdentity.readBackFailed");

    internal static async Task<bool> TryStageProjectionAsync(
        IServiceIdentityClientProjection projection,
        ServiceIdentity identity,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        try
        {
            await projection.StageAsync(identity, ct);
            return true;
        }
        catch (ServiceIdentityClientProjectionException)
        {
            uow.ClearTracking();
            return false;
        }
    }

    internal static async Task<bool> TryEnqueueAsync(
        IIdentityAuditOutbox audit,
        AuditEventV1 auditEvent,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        try
        {
            await audit.EnqueueAsync(auditEvent, ct);
            return true;
        }
        catch (Exception)
        {
            uow.ClearTracking();
            return false;
        }
    }

    internal static async Task<Result<ServiceIdentityDto>> PersistDenied(
        Guid actorId,
        Guid workspaceId,
        Guid targetId,
        string action,
        string correlationId,
        IIdentityAuditOutbox audit,
        TimeProvider clock,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        AuditEventV1 auditEvent = new AuditEventV1(
            Guid.NewGuid(),
            AuditActorKindV1.Human,
            actorId,
            actorId,
            workspaceId,
            action,
            "ServiceIdentity",
            targetId,
            "authority_denied",
            clock.GetUtcNow(),
            correlationId.Trim(),
            new Dictionary<string, string> { ["workspaceId"] = workspaceId.ToString() });

        if (!await TryEnqueueAsync(audit, auditEvent, uow, ct))
            return AuditUnavailable();

        try
        {
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
            IdentityAuditOutboxEntry? persisted = await audit.GetAsync(auditEvent.EventId, ct);
            return IsExpectedAudit(persisted, auditEvent, requireCorrelation: true)
                ? Forbidden()
                : AuditUnavailable();
        }
        catch (Exception)
        {
            uow.ClearTracking();
            return AuditUnavailable();
        }
    }

    private static bool IsExpectedAudit(
        IdentityAuditOutboxEntry? entry,
        AuditEventV1 expected,
        bool requireCorrelation)
    {
        if (entry is null
            || entry.State is not (IdentityAuditOutboxState.Pending or IdentityAuditOutboxState.Delivered))
        {
            return false;
        }

        AuditEventV1 actual = entry.Event;
        return actual.EventId == expected.EventId
            && actual.ActorKind == expected.ActorKind
            && actual.ActorId == expected.ActorId
            && actual.SubjectId == expected.SubjectId
            && actual.WorkspaceId == expected.WorkspaceId
            && StringComparer.Ordinal.Equals(actual.Action, expected.Action)
            && StringComparer.Ordinal.Equals(actual.TargetType, expected.TargetType)
            && actual.TargetId == expected.TargetId
            && StringComparer.Ordinal.Equals(actual.Outcome, expected.Outcome)
            && (!requireCorrelation || StringComparer.Ordinal.Equals(actual.CorrelationId, expected.CorrelationId))
            && (!requireCorrelation
                || Math.Abs((actual.OccurredAt - expected.OccurredAt).Ticks) < TimeSpan.TicksPerMillisecond)
            && !string.IsNullOrWhiteSpace(actual.CorrelationId)
            && ContainsExpectedMetadata(actual.Metadata, expected.Metadata, requireCorrelation);
    }

    private static bool ContainsExpectedMetadata(
        IReadOnlyDictionary<string, string>? actual,
        IReadOnlyDictionary<string, string>? expected,
        bool requireExact)
    {
        if (actual is null || expected is null)
            return actual is null && expected is null;

        foreach ((string key, string value) in expected)
        {
            if (!requireExact && key == "status")
                continue;
            if (!actual.TryGetValue(key, out string? actualValue)
                || !StringComparer.Ordinal.Equals(actualValue, value))
            {
                return false;
            }
        }

        return actual.Count == expected.Count
            && actual.Keys.All(key => key is "workspaceId" or "serviceIdentityId" or "status" or "keyId");
    }

    private static Guid LifecycleEventId(Guid identityId, string action, Guid? keyId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"service-identity-lifecycle:{identityId:D}:{action}:{keyId?.ToString("D") ?? "identity"}"));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16));
    }
}

public sealed class AddServiceIdentityKeyHandler(
    IWorkspaceMembershipRepository memberships,
    IServiceIdentityRepository identities,
    IIdentityAuditOutbox audit,
    TimeProvider clock,
    IUnitOfWork uow,
    IServiceIdentityClientProjection projection)
    : ICommandHandler<AddServiceIdentityKeyCommand, ServiceIdentityDto>
{
    public async Task<Result<ServiceIdentityDto>> Handle(AddServiceIdentityKeyCommand c, CancellationToken ct)
    {
        ServiceIdentity? identity = await identities.GetAsync(c.WorkspaceId, c.ServiceIdentityId, ct);
        if (identity is null)
            return CreateServiceIdentityHandler.Conflict();
        if (!await CreateServiceIdentityHandler.IsAdministrator(memberships, c.WorkspaceId, c.ActorUserId, ct))
        {
            return await CreateServiceIdentityHandler.PersistDenied(
                c.ActorUserId,
                c.WorkspaceId,
                identity.Id,
                "service_identity.key_add_denied",
                c.CorrelationId,
                audit,
                clock,
                uow,
                ct);
        }
        if (!ServiceIdentityPublicJwkParser.TryParse(c.PublicJwk, out ServiceIdentityPublicJwk? parsed))
        {
            return Result.Failure<ServiceIdentityDto>(
                ErrorCodes.InvalidInput,
                "A public ES256 JWK is required.",
                "identity.serviceIdentity.invalidJwk");
        }

        ServiceIdentityKey? prior = identity.Keys.SingleOrDefault(
            key => key.Status == ServiceIdentityKeyStatus.Active
                && key.Kid == parsed!.Kid
                && key.Thumbprint == parsed.Thumbprint);
        if (prior is not null && identity.Status == ServiceIdentityStatus.Active)
        {
            return await CreateServiceIdentityHandler.ReadCanonical(
                identity,
                CreateServiceIdentityHandler.Event(c.ActorUserId, identity, "service_identity.key_added", "succeeded", c.CorrelationId, clock, prior.Id),
                identities,
                audit,
                ct);
        }

        ServiceIdentityKey key;
        try
        {
            key = identity.AddKey(
                parsed!.Kid,
                parsed.Thumbprint,
                parsed.X,
                parsed.Y,
                c.ExpectedRevision,
                clock.GetUtcNow().UtcDateTime);
        }
        catch (InvalidOperationException)
        {
            return CreateServiceIdentityHandler.Conflict();
        }

        if (!await CreateServiceIdentityHandler.TryStageProjectionAsync(projection, identity, uow, ct))
            return CreateServiceIdentityHandler.Conflict();

        AuditEventV1 auditEvent = CreateServiceIdentityHandler.Event(
            c.ActorUserId,
            identity,
            "service_identity.key_added",
            "succeeded",
            c.CorrelationId,
            clock,
            key.Id);
        if (!await CreateServiceIdentityHandler.TryEnqueueAsync(audit, auditEvent, uow, ct))
            return CreateServiceIdentityHandler.AuditUnavailable();

        return await CreateServiceIdentityHandler.SaveAndRead(
            identity,
            auditEvent,
            identities,
            audit,
            uow,
            async () =>
            {
                ServiceIdentity? current = await identities.GetAsync(c.WorkspaceId, c.ServiceIdentityId, ct);
                ServiceIdentityKey? concurrent = current?.Keys.SingleOrDefault(
                    value => value.Status == ServiceIdentityKeyStatus.Active
                        && value.Kid == parsed.Kid
                        && value.Thumbprint == parsed.Thumbprint);
                return current is not null && concurrent is not null
                    ? await CreateServiceIdentityHandler.ReadCanonical(
                        current,
                        CreateServiceIdentityHandler.Event(c.ActorUserId, current, "service_identity.key_added", "succeeded", c.CorrelationId, clock, concurrent.Id),
                        identities,
                        audit,
                        ct)
                    : CreateServiceIdentityHandler.Conflict();
            },
            ct);
    }
}

public sealed class RevokeServiceIdentityKeyHandler(
    IWorkspaceMembershipRepository memberships,
    IServiceIdentityRepository identities,
    IIdentityAuditOutbox audit,
    TimeProvider clock,
    IUnitOfWork uow,
    IServiceIdentityClientProjection projection)
    : ICommandHandler<RevokeServiceIdentityKeyCommand, ServiceIdentityDto>
{
    public async Task<Result<ServiceIdentityDto>> Handle(RevokeServiceIdentityKeyCommand c, CancellationToken ct)
    {
        ServiceIdentity? identity = await identities.GetAsync(c.WorkspaceId, c.ServiceIdentityId, ct);
        if (identity is null)
            return CreateServiceIdentityHandler.Conflict();
        if (!await CreateServiceIdentityHandler.IsAdministrator(memberships, c.WorkspaceId, c.ActorUserId, ct))
        {
            return await CreateServiceIdentityHandler.PersistDenied(
                c.ActorUserId,
                c.WorkspaceId,
                identity.Id,
                "service_identity.key_revoke_denied",
                c.CorrelationId,
                audit,
                clock,
                uow,
                ct);
        }

        ServiceIdentityKey? key = identity.Keys.SingleOrDefault(value => value.Id == c.KeyId);
        if (key is null)
            return CreateServiceIdentityHandler.Conflict();

        bool changed;
        try
        {
            changed = identity.RevokeKey(c.KeyId, c.ExpectedRevision, clock.GetUtcNow().UtcDateTime);
        }
        catch (InvalidOperationException)
        {
            return CreateServiceIdentityHandler.Conflict();
        }

        AuditEventV1 auditEvent = CreateServiceIdentityHandler.Event(
            c.ActorUserId,
            identity,
            "service_identity.key_revoked",
            "succeeded",
            c.CorrelationId,
            clock,
            key.Id);
        if (!changed)
        {
            return await CreateServiceIdentityHandler.ReadCanonical(identity, auditEvent, identities, audit, ct);
        }

        if (!await CreateServiceIdentityHandler.TryStageProjectionAsync(projection, identity, uow, ct))
            return CreateServiceIdentityHandler.Conflict();
        if (!await CreateServiceIdentityHandler.TryEnqueueAsync(audit, auditEvent, uow, ct))
            return CreateServiceIdentityHandler.AuditUnavailable();

        return await CreateServiceIdentityHandler.SaveAndRead(
            identity,
            auditEvent,
            identities,
            audit,
            uow,
            async () =>
            {
                ServiceIdentity? current = await identities.GetAsync(c.WorkspaceId, c.ServiceIdentityId, ct);
                ServiceIdentityKey? currentKey = current?.Keys.SingleOrDefault(value => value.Id == c.KeyId);
                return current is not null && currentKey?.Status == ServiceIdentityKeyStatus.Revoked
                    ? await CreateServiceIdentityHandler.ReadCanonical(
                        current,
                        CreateServiceIdentityHandler.Event(c.ActorUserId, current, "service_identity.key_revoked", "succeeded", c.CorrelationId, clock, currentKey.Id),
                        identities,
                        audit,
                        ct)
                    : CreateServiceIdentityHandler.Conflict();
            },
            ct);
    }
}

public sealed class RevokeServiceIdentityHandler(
    IWorkspaceMembershipRepository memberships,
    IServiceIdentityRepository identities,
    IIdentityAuditOutbox audit,
    TimeProvider clock,
    IUnitOfWork uow,
    IServiceIdentityClientProjection projection)
    : ICommandHandler<RevokeServiceIdentityCommand, ServiceIdentityDto>
{
    public async Task<Result<ServiceIdentityDto>> Handle(RevokeServiceIdentityCommand c, CancellationToken ct)
    {
        ServiceIdentity? identity = await identities.GetAsync(c.WorkspaceId, c.ServiceIdentityId, ct);
        if (identity is null)
            return CreateServiceIdentityHandler.Conflict();
        if (!await CreateServiceIdentityHandler.IsAdministrator(memberships, c.WorkspaceId, c.ActorUserId, ct))
        {
            return await CreateServiceIdentityHandler.PersistDenied(
                c.ActorUserId,
                c.WorkspaceId,
                identity.Id,
                "service_identity.revoke_denied",
                c.CorrelationId,
                audit,
                clock,
                uow,
                ct);
        }

        bool changed;
        try
        {
            changed = identity.Revoke(c.ExpectedRevision, clock.GetUtcNow().UtcDateTime);
        }
        catch (InvalidOperationException)
        {
            return CreateServiceIdentityHandler.Conflict();
        }

        AuditEventV1 auditEvent = CreateServiceIdentityHandler.Event(
            c.ActorUserId,
            identity,
            "service_identity.revoked",
            "succeeded",
            c.CorrelationId,
            clock);
        if (!changed)
            return await CreateServiceIdentityHandler.ReadCanonical(identity, auditEvent, identities, audit, ct);

        if (!await CreateServiceIdentityHandler.TryStageProjectionAsync(projection, identity, uow, ct))
            return CreateServiceIdentityHandler.Conflict();
        if (!await CreateServiceIdentityHandler.TryEnqueueAsync(audit, auditEvent, uow, ct))
            return CreateServiceIdentityHandler.AuditUnavailable();

        return await CreateServiceIdentityHandler.SaveAndRead(
            identity,
            auditEvent,
            identities,
            audit,
            uow,
            async () =>
            {
                ServiceIdentity? current = await identities.GetAsync(c.WorkspaceId, c.ServiceIdentityId, ct);
                return current?.Status == ServiceIdentityStatus.Revoked
                    ? await CreateServiceIdentityHandler.ReadCanonical(
                        current,
                        CreateServiceIdentityHandler.Event(c.ActorUserId, current, "service_identity.revoked", "succeeded", c.CorrelationId, clock),
                        identities,
                        audit,
                        ct)
                    : CreateServiceIdentityHandler.Conflict();
            },
            ct);
    }
}
