using System.Security.Cryptography;
using System.Text.Json;
using Axis.Audit.Contracts;
using Axis.Identity.Application;
using Axis.Identity.Application.Commands.ManageServiceIdentity;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class ManageServiceIdentityHandlerTests
{
    [Fact]
    public async Task Create_WhenAuthorityMissing_PersistsAndReadsRedactedDenialBeforeForbidden()
    {
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        MemoryAuditOutbox audits = new MemoryAuditOutbox();
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();

        Result<ServiceIdentityDto> result = await new CreateServiceIdentityHandler(
                memberships,
                Substitute.For<IServiceIdentityRepository>(),
                audits,
                TimeProvider.System,
                uow,
                Substitute.For<IServiceIdentityClientProjection>())
            .Handle(
                new CreateServiceIdentityCommand(Guid.NewGuid(), Guid.NewGuid(), "svc-a", "corr", "Axis Admin"),
                CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        AuditEventV1 audit = audits.Events.Should().ContainSingle().Subject;
        audit.Outcome.Should().Be("authority_denied");
        audit.Metadata.Should().NotContainKey("assertion");
        audit.Metadata.Should().NotContainKey("token");
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WhenDenialAuditCannotPersist_FailsClosed()
    {
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException());

        Result<ServiceIdentityDto> result = await new CreateServiceIdentityHandler(
                memberships,
                Substitute.For<IServiceIdentityRepository>(),
                new MemoryAuditOutbox(),
                TimeProvider.System,
                uow,
                Substitute.For<IServiceIdentityClientProjection>())
            .Handle(
                new CreateServiceIdentityCommand(Guid.NewGuid(), Guid.NewGuid(), "svc-a", "corr", "Axis Admin"),
                CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.ProblemCode.Should().Be("identity.serviceIdentity.auditUnavailable");
    }

    [Fact]
    public async Task Create_WhenAuthorityActive_StagesProjectionAndConfirmsAuditReadBack()
    {
        Guid actorId = Guid.NewGuid();
        Guid workspaceId = Guid.NewGuid();
        IWorkspaceMembershipRepository memberships = ActiveAdministrator(actorId, workspaceId);
        IServiceIdentityRepository identities = Substitute.For<IServiceIdentityRepository>();
        ServiceIdentity? stagedIdentity = null;
        identities.AddAsync(Arg.Any<ServiceIdentity>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                stagedIdentity = call.Arg<ServiceIdentity>();
                return Task.CompletedTask;
            });
        identities.GetAsync(workspaceId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => stagedIdentity);
        IServiceIdentityClientProjection projection = Substitute.For<IServiceIdentityClientProjection>();

        Result<ServiceIdentityDto> result = await new CreateServiceIdentityHandler(
                memberships,
                identities,
                new MemoryAuditOutbox(),
                TimeProvider.System,
                Substitute.For<IUnitOfWork>(),
                projection)
            .Handle(
                new CreateServiceIdentityCommand(actorId, workspaceId, "svc-a", "corr", "Axis Admin"),
                TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Subject.Should().Be(new SubjectReferenceDto(SubjectKind.Service, result.Value.Id));
        result.Value.WorkspaceId.Should().Be(workspaceId);
        result.Value.WorkspaceGrantStatus.Should().Be(nameof(ServiceWorkspaceGrantStatus.Active));
        stagedIdentity.Should().NotBeNull();
        stagedIdentity!.Id.Should().Be(result.Value.Id);
        stagedIdentity.WorkspaceId.Should().Be(workspaceId);
        stagedIdentity.WorkspaceGrantStatus.Should().Be(ServiceWorkspaceGrantStatus.Active);
        await memberships.Received(1).GetActiveAsync(
            workspaceId,
            actorId,
            Arg.Any<CancellationToken>());
        await memberships.DidNotReceive().AddAsync(
            Arg.Any<WorkspaceMembership>(),
            Arg.Any<CancellationToken>());
        typeof(CreateServiceIdentityHandler).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should().NotContain(typeof(IOrganizationMembershipRepository));
        await projection.Received(1).StageAsync(
            Arg.Is<ServiceIdentity>(identity => identity == stagedIdentity),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeyLifecycle_WhenRevokeIsRetried_ReturnsCanonicalAndRetainsTombstones()
    {
        Guid actorId = Guid.NewGuid();
        Guid workspaceId = Guid.NewGuid();
        ServiceIdentity identity = ServiceIdentity.Create(workspaceId, "svc-key-lifecycle", DateTime.UtcNow);
        identity.InitializeMetadata(ActorSnapshot.User(actorId, "Axis Admin"));
        IServiceIdentityRepository identities = RepositoryFor(identity);
        MemoryAuditOutbox audits = new MemoryAuditOutbox();
        IServiceIdentityClientProjection projection = Substitute.For<IServiceIdentityClientProjection>();
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        IWorkspaceMembershipRepository memberships = ActiveAdministrator(actorId, workspaceId);
        string firstJwk = PublicJwk("key-a");

        Result<ServiceIdentityDto> added = await new AddServiceIdentityKeyHandler(
            memberships, identities, audits, TimeProvider.System, uow, projection).Handle(
                new AddServiceIdentityKeyCommand(actorId, workspaceId, identity.Id, 1, firstJwk, "add-corr", "Axis Admin"),
                TestContext.Current.CancellationToken);
        Result<ServiceIdentityDto> overlap = await new AddServiceIdentityKeyHandler(
            memberships, identities, audits, TimeProvider.System, uow, projection).Handle(
                new AddServiceIdentityKeyCommand(actorId, workspaceId, identity.Id, 2, PublicJwk("key-b"), "overlap-corr", "Axis Admin"),
                TestContext.Current.CancellationToken);
        Guid firstKeyId = identity.Keys.Single(key => key.Kid == "key-a").Id;
        int revokeRevision = identity.Revision;
        Result<ServiceIdentityDto> revoked = await new RevokeServiceIdentityKeyHandler(
            memberships, identities, audits, TimeProvider.System, uow, projection).Handle(
                new RevokeServiceIdentityKeyCommand(actorId, workspaceId, identity.Id, firstKeyId, revokeRevision, "revoke-corr", "Axis Admin"),
                TestContext.Current.CancellationToken);
        Result<ServiceIdentityDto> retry = await new RevokeServiceIdentityKeyHandler(
            memberships, identities, audits, TimeProvider.System, uow, projection).Handle(
                new RevokeServiceIdentityKeyCommand(actorId, workspaceId, identity.Id, firstKeyId, revokeRevision, "retry-corr", "Axis Admin"),
                TestContext.Current.CancellationToken);

        added.IsSuccess.Should().BeTrue();
        overlap.IsSuccess.Should().BeTrue();
        revoked.IsSuccess.Should().BeTrue();
        retry.IsSuccess.Should().BeTrue();
        retry.Value.Revision.Should().Be(revoked.Value.Revision);
        identity.Keys.Should().ContainSingle(key => key.Kid == "key-b" && key.Status == ServiceIdentityKeyStatus.Active);
        identity.Tombstones.Should().ContainSingle(value => value.Kid == "key-a");
        audits.Events.Count(value => value.Action == "service_identity.key_revoked").Should().Be(1);

        Result<ServiceIdentityDto> resurrection = await new AddServiceIdentityKeyHandler(
            memberships, identities, audits, TimeProvider.System, uow, projection).Handle(
                new AddServiceIdentityKeyCommand(
                    actorId,
                    workspaceId,
                    identity.Id,
                    identity.Revision,
                    RenameKid(firstJwk, "key-c"),
                    "resurrection-corr",
                    "Axis Admin"),
                TestContext.Current.CancellationToken);
        resurrection.IsFailure.Should().BeTrue();
        resurrection.ProblemCode.Should().Be("identity.serviceIdentity.conflict");
    }

    [Fact]
    public async Task RevokeIdentity_WhenRetriedWithStaleRevision_ReturnsOneCanonicalTerminalOutcome()
    {
        Guid actorId = Guid.NewGuid();
        Guid workspaceId = Guid.NewGuid();
        ServiceIdentity identity = ServiceIdentity.Create(workspaceId, "svc-revoke", DateTime.UtcNow);
        identity.InitializeMetadata(ActorSnapshot.User(actorId, "Axis Admin"));
        IServiceIdentityRepository identities = RepositoryFor(identity);
        IWorkspaceMembershipRepository memberships = ActiveAdministrator(actorId, workspaceId);
        MemoryAuditOutbox audits = new MemoryAuditOutbox();
        RevokeServiceIdentityHandler handler = new RevokeServiceIdentityHandler(
            memberships,
            identities,
            audits,
            TimeProvider.System,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IServiceIdentityClientProjection>());

        Result<ServiceIdentityDto> first = await handler.Handle(
            new RevokeServiceIdentityCommand(actorId, workspaceId, identity.Id, 1, "first-corr", "Axis Admin"),
            TestContext.Current.CancellationToken);
        Result<ServiceIdentityDto> retry = await handler.Handle(
            new RevokeServiceIdentityCommand(actorId, workspaceId, identity.Id, 1, "retry-corr", "Axis Admin"),
            TestContext.Current.CancellationToken);

        first.IsSuccess.Should().BeTrue();
        retry.IsSuccess.Should().BeTrue();
        retry.Value.Revision.Should().Be(first.Value.Revision);
        retry.Value.WorkspaceGrantStatus.Should().Be(nameof(ServiceWorkspaceGrantStatus.Revoked));
        audits.Events.Count(value => value.Action == "service_identity.revoked").Should().Be(1);
    }

    [Fact]
    public async Task Create_WhenAuditReadBackIsMissing_DoesNotReportSuccess()
    {
        Guid actorId = Guid.NewGuid();
        Guid workspaceId = Guid.NewGuid();
        ServiceIdentity? identity = null;
        IServiceIdentityRepository identities = Substitute.For<IServiceIdentityRepository>();
        identities.AddAsync(Arg.Any<ServiceIdentity>(), Arg.Any<CancellationToken>())
            .Returns(call => { identity = call.Arg<ServiceIdentity>(); return Task.CompletedTask; });
        identities.GetAsync(workspaceId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => identity);

        Result<ServiceIdentityDto> result = await new CreateServiceIdentityHandler(
            ActiveAdministrator(actorId, workspaceId),
            identities,
            new MissingReadBackAuditOutbox(),
            TimeProvider.System,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IServiceIdentityClientProjection>()).Handle(
                new CreateServiceIdentityCommand(actorId, workspaceId, "svc-no-audit-read", "corr", "Axis Admin"),
                TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ProblemCode.Should().Be("identity.serviceIdentity.readBackFailed");
    }

    private static IWorkspaceMembershipRepository ActiveAdministrator(Guid actorId, Guid workspaceId)
    {
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        memberships.GetActiveAsync(workspaceId, actorId, Arg.Any<CancellationToken>())
            .Returns(WorkspaceMembership.CreateOrganizationMember(
                workspaceId,
                actorId,
                WorkspaceMembershipRole.Administrator));
        return memberships;
    }

    private static IServiceIdentityRepository RepositoryFor(ServiceIdentity identity)
    {
        IServiceIdentityRepository identities = Substitute.For<IServiceIdentityRepository>();
        identities.GetAsync(identity.WorkspaceId, identity.Id, Arg.Any<CancellationToken>())
            .Returns(identity);
        identities.GetByClientIdAsync(identity.ClientId, Arg.Any<CancellationToken>())
            .Returns(identity);
        return identities;
    }

    private static string PublicJwk(string kid)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters parameters = key.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            kty = "EC",
            crv = "P-256",
            kid,
            x = Base64Url.Encode(parameters.Q.X!),
            y = Base64Url.Encode(parameters.Q.Y!),
        });
    }

    private static string RenameKid(string jwk, string kid)
    {
        using JsonDocument document = JsonDocument.Parse(jwk);
        JsonElement root = document.RootElement;
        return JsonSerializer.Serialize(new
        {
            kty = root.GetProperty("kty").GetString(),
            crv = root.GetProperty("crv").GetString(),
            kid,
            x = root.GetProperty("x").GetString(),
            y = root.GetProperty("y").GetString(),
        });
    }

    private sealed class MemoryAuditOutbox : IIdentityAuditOutbox
    {
        private readonly Dictionary<Guid, AuditEventV1> _events = [];
        public IReadOnlyCollection<AuditEventV1> Events => _events.Values;

        public Task EnqueueAsync(AuditEventV1 auditEvent, CancellationToken ct = default)
        {
            _events.Add(auditEvent.EventId, auditEvent);
            return Task.CompletedTask;
        }

        public Task<IdentityAuditOutboxEntry?> GetAsync(Guid eventId, CancellationToken ct = default) =>
            Task.FromResult(_events.TryGetValue(eventId, out AuditEventV1? value)
                ? new IdentityAuditOutboxEntry(value, IdentityAuditOutboxState.Pending)
                : null);
    }

    private sealed class MissingReadBackAuditOutbox : IIdentityAuditOutbox
    {
        public Task EnqueueAsync(AuditEventV1 auditEvent, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IdentityAuditOutboxEntry?> GetAsync(Guid eventId, CancellationToken ct = default) =>
            Task.FromResult<IdentityAuditOutboxEntry?>(null);
    }
}
