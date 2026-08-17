using System.Security.Cryptography;
using System.Text.Json;
using Axis.Audit.Contracts;
using Axis.Identity.Application;
using Axis.Identity.Application.Commands.ManageServiceIdentity;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Services;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Tests.Commands;

[Collection("IdentityDb")]
public sealed class ManageServiceIdentityIntegrationTests(IdentityDatabaseFixture database)
{
    [Fact]
    public async Task KeyLifecycle_WhenValid_PersistsGrantKeysTombstoneAndRedactedAudit()
    {
        (Guid actorId, Guid workspaceId) = await SeedAdministratorAsync();
        Result<ServiceIdentityDto> created = await CreateAsync(actorId, workspaceId, "svc-lifecycle");
        Result<ServiceIdentityDto> first = await AddKeyAsync(
            actorId,
            workspaceId,
            created.Value.Id,
            created.Value.Revision,
            PublicJwk("key-a"),
            "add-a");
        Result<ServiceIdentityDto> overlap = await AddKeyAsync(
            actorId,
            workspaceId,
            created.Value.Id,
            first.Value.Revision,
            PublicJwk("key-b"),
            "add-b");
        ServiceIdentityKeyDto firstKey = overlap.Value.Keys.Single(value => value.Kid == "key-a");
        Result<ServiceIdentityDto> revoked = await RevokeKeyAsync(
            actorId,
            workspaceId,
            created.Value.Id,
            firstKey.Id,
            overlap.Value.Revision,
            "revoke-a");

        created.IsSuccess.Should().BeTrue();
        first.IsSuccess.Should().BeTrue();
        overlap.IsSuccess.Should().BeTrue();
        revoked.IsSuccess.Should().BeTrue();
        revoked.Value.Keys.Should().ContainSingle(value => value.Kid == "key-b" && value.Status == "Active");

        await using IdentityDbContext observer = database.CreateContext();
        ServiceIdentity stored = await observer.ServiceIdentities.SingleAsync(
            value => value.Id == created.Value.Id,
            TestContext.Current.CancellationToken);
        stored.WorkspaceId.Should().Be(workspaceId);
        stored.WorkspaceGrantStatus.Should().Be(ServiceWorkspaceGrantStatus.Active);
        stored.Keys.Should().HaveCount(2);
        stored.Tombstones.Should().ContainSingle(value => value.Kid == "key-a");

        List<IdentityAuditOutboxRecord> audits = await observer.Set<IdentityAuditOutboxRecord>()
            .Where(value => value.TargetId == stored.Id)
            .OrderBy(value => value.Action)
            .ToListAsync(TestContext.Current.CancellationToken);
        audits.Select(value => value.Action).Should().BeEquivalentTo(
            "service_identity.created",
            "service_identity.key_added",
            "service_identity.key_added",
            "service_identity.key_revoked");
        audits.Should().OnlyContain(value => value.Status == IdentityAuditOutboxStatus.Pending);
        audits.Should().OnlyContain(value => !value.MetadataJson.Contains("assertion", StringComparison.OrdinalIgnoreCase));
        audits.Should().OnlyContain(value => !value.MetadataJson.Contains("token", StringComparison.OrdinalIgnoreCase));
        audits.Should().OnlyContain(value => !value.MetadataJson.Contains("\"d\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddKey_WhenSameRequestRaces_ReturnsOneCanonicalKeyAndAudit()
    {
        (Guid actorId, Guid workspaceId) = await SeedAdministratorAsync();
        Result<ServiceIdentityDto> created = await CreateAsync(actorId, workspaceId, "svc-add-race");
        string jwk = PublicJwk("race-key");
        using Barrier barrier = new(2);

        Task<Result<ServiceIdentityDto>> first = Task.Run(() => AddKeyAsync(
            actorId,
            workspaceId,
            created.Value.Id,
            created.Value.Revision,
            jwk,
            "race-one",
            projectionFactory: context => new CoordinatedProjection(new ServiceIdentityClientProjection(context), barrier)));
        Task<Result<ServiceIdentityDto>> second = Task.Run(() => AddKeyAsync(
            actorId,
            workspaceId,
            created.Value.Id,
            created.Value.Revision,
            jwk,
            "race-two",
            projectionFactory: context => new CoordinatedProjection(new ServiceIdentityClientProjection(context), barrier)));

        Result<ServiceIdentityDto>[] results = await Task.WhenAll(first, second);

        results.Should().OnlyContain(value => value.IsSuccess);
        results[0].Value.Keys.Single().Id.Should().Be(results[1].Value.Keys.Single().Id);
        await using IdentityDbContext observer = database.CreateContext();
        ServiceIdentity stored = await observer.ServiceIdentities.SingleAsync(
            value => value.Id == created.Value.Id,
            TestContext.Current.CancellationToken);
        stored.Keys.Should().ContainSingle(value => value.Kid == "race-key");
        (await observer.Set<IdentityAuditOutboxRecord>().CountAsync(
            value => value.TargetId == stored.Id && value.Action == "service_identity.key_added",
            TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task RevokeKey_WhenSameRequestRaces_ReturnsOneCanonicalTombstoneAndAudit()
    {
        (Guid actorId, Guid workspaceId) = await SeedAdministratorAsync();
        Result<ServiceIdentityDto> created = await CreateAsync(actorId, workspaceId, "svc-revoke-race");
        Result<ServiceIdentityDto> added = await AddKeyAsync(
            actorId,
            workspaceId,
            created.Value.Id,
            created.Value.Revision,
            PublicJwk("revoke-key"),
            "add-key");
        Guid keyId = added.Value.Keys.Single().Id;
        using Barrier barrier = new(2);

        Task<Result<ServiceIdentityDto>> first = Task.Run(() => RevokeKeyAsync(
            actorId,
            workspaceId,
            created.Value.Id,
            keyId,
            added.Value.Revision,
            "race-one",
            context => new CoordinatedProjection(new ServiceIdentityClientProjection(context), barrier)));
        Task<Result<ServiceIdentityDto>> second = Task.Run(() => RevokeKeyAsync(
            actorId,
            workspaceId,
            created.Value.Id,
            keyId,
            added.Value.Revision,
            "race-two",
            context => new CoordinatedProjection(new ServiceIdentityClientProjection(context), barrier)));

        Result<ServiceIdentityDto>[] results = await Task.WhenAll(first, second);

        results.Should().OnlyContain(value => value.IsSuccess);
        results[0].Value.Revision.Should().Be(results[1].Value.Revision);
        await using IdentityDbContext observer = database.CreateContext();
        ServiceIdentity stored = await observer.ServiceIdentities.SingleAsync(
            value => value.Id == created.Value.Id,
            TestContext.Current.CancellationToken);
        stored.Tombstones.Should().ContainSingle(value => value.Kid == "revoke-key");
        (await observer.Set<IdentityAuditOutboxRecord>().CountAsync(
            value => value.TargetId == stored.Id && value.Action == "service_identity.key_revoked",
            TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Create_WhenAuditCannotBeStaged_PersistsNoLifecycleOrProjection()
    {
        (Guid actorId, Guid workspaceId) = await SeedAdministratorAsync();

        Result<ServiceIdentityDto> result = await CreateAsync(
            actorId,
            workspaceId,
            "svc-no-audit",
            _ => new ThrowingAuditOutbox());

        result.IsFailure.Should().BeTrue();
        result.ProblemCode.Should().Be("identity.serviceIdentity.auditUnavailable");
        await using IdentityDbContext observer = database.CreateContext();
        (await observer.ServiceIdentities.CountAsync(
            value => value.ClientId == "svc-no-audit",
            TestContext.Current.CancellationToken)).Should().Be(0);
        (await observer.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreApplication>().CountAsync(
            value => value.ClientId == "svc-no-audit",
            TestContext.Current.CancellationToken)).Should().Be(0);
        (await observer.Set<IdentityAuditOutboxRecord>().CountAsync(
            value => value.Action == "service_identity.created" && value.ActorId == actorId,
            TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Create_WhenAuthorityIsMissing_PersistsRedactedDenialWithoutLifecycleMutation()
    {
        (Guid _, Guid workspaceId) = await SeedAdministratorAsync();
        Guid deniedActorId = Guid.NewGuid();

        Result<ServiceIdentityDto> result = await CreateAsync(
            deniedActorId,
            workspaceId,
            "svc-denied");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        await using IdentityDbContext observer = database.CreateContext();
        (await observer.ServiceIdentities.CountAsync(
            value => value.ClientId == "svc-denied",
            TestContext.Current.CancellationToken)).Should().Be(0);
        IdentityAuditOutboxRecord audit = await observer.Set<IdentityAuditOutboxRecord>().SingleAsync(
            value => value.ActorId == deniedActorId && value.Action == "service_identity.create_denied",
            TestContext.Current.CancellationToken);
        audit.Outcome.Should().Be("authority_denied");
        audit.WorkspaceId.Should().Be(workspaceId);
        JsonSerializer.Deserialize<Dictionary<string, string>>(audit.MetadataJson).Should().BeEquivalentTo(
            new Dictionary<string, string> { ["workspaceId"] = workspaceId.ToString() });
        audit.MetadataJson.Contains("assertion", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        audit.MetadataJson.Contains("token", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    [Fact]
    public async Task Create_WhenAuditReadBackIsUnavailable_FailsClosedAndCanonicalRetryRecovers()
    {
        (Guid actorId, Guid workspaceId) = await SeedAdministratorAsync();

        Result<ServiceIdentityDto> uncertain = await CreateAsync(
            actorId,
            workspaceId,
            "svc-readback",
            context => new MissingReadBackAuditOutbox(new IdentityAuditOutbox(context)));
        Result<ServiceIdentityDto> recovered = await CreateAsync(
            actorId,
            workspaceId,
            "svc-readback");

        uncertain.IsFailure.Should().BeTrue();
        uncertain.ProblemCode.Should().Be("identity.serviceIdentity.readBackFailed");
        recovered.IsSuccess.Should().BeTrue();
        await using IdentityDbContext observer = database.CreateContext();
        (await observer.ServiceIdentities.CountAsync(
            value => value.ClientId == "svc-readback",
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await observer.Set<IdentityAuditOutboxRecord>().CountAsync(
            value => value.TargetId == recovered.Value.Id && value.Action == "service_identity.created",
            TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task AddKey_WhenRevokedMaterialIsRenamed_ConflictsWithoutMutation()
    {
        (Guid actorId, Guid workspaceId) = await SeedAdministratorAsync();
        Result<ServiceIdentityDto> created = await CreateAsync(actorId, workspaceId, "svc-tombstone");
        string jwk = PublicJwk("original-kid");
        Result<ServiceIdentityDto> added = await AddKeyAsync(
            actorId, workspaceId, created.Value.Id, created.Value.Revision, jwk, "add");
        Result<ServiceIdentityDto> revoked = await RevokeKeyAsync(
            actorId,
            workspaceId,
            created.Value.Id,
            added.Value.Keys.Single().Id,
            added.Value.Revision,
            "revoke");

        Result<ServiceIdentityDto> result = await AddKeyAsync(
            actorId,
            workspaceId,
            created.Value.Id,
            revoked.Value.Revision,
            RenameKid(jwk, "renamed-kid"),
            "reuse");

        result.IsFailure.Should().BeTrue();
        result.ProblemCode.Should().Be("identity.serviceIdentity.conflict");
        await using IdentityDbContext observer = database.CreateContext();
        ServiceIdentity stored = await observer.ServiceIdentities.SingleAsync(
            value => value.Id == created.Value.Id,
            TestContext.Current.CancellationToken);
        stored.Keys.Should().ContainSingle();
        stored.Tombstones.Should().ContainSingle();
        (await observer.Set<IdentityAuditOutboxRecord>().CountAsync(
            value => value.TargetId == stored.Id && value.Action == "service_identity.key_added",
            TestContext.Current.CancellationToken)).Should().Be(1);
    }

    private async Task<(Guid ActorId, Guid WorkspaceId)> SeedAdministratorAsync()
    {
        User actor = User.Create(
            "Service Identity Administrator",
            Email.Create($"service-admin-{Guid.NewGuid():N}@example.com").Value);
        actor.VerifyEmail();
        Organization organization = Organization.Create($"Service Org {Guid.NewGuid():N}");
        Workspace workspace = Workspace.CreateOrganization(
            "Service Workspace",
            WorkspaceSlug.Create($"service-{Guid.NewGuid():N}").Value,
            organization.Id);
        WorkspaceMembership membership = WorkspaceMembership.CreateOrganizationMember(
            workspace.Id,
            actor.Id,
            WorkspaceMembershipRole.Administrator);
        await using IdentityDbContext context = database.CreateContext();
        context.Users.Add(actor);
        context.Organizations.Add(organization);
        context.Workspaces.Add(workspace);
        context.WorkspaceMemberships.Add(membership);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (actor.Id, workspace.Id);
    }

    private async Task<Result<ServiceIdentityDto>> CreateAsync(
        Guid actorId,
        Guid workspaceId,
        string clientId,
        Func<IdentityDbContext, IIdentityAuditOutbox>? auditFactory = null)
    {
        await using IdentityDbContext context = database.CreateContext();
        CreateServiceIdentityHandler handler = new CreateServiceIdentityHandler(
            new WorkspaceMembershipRepository(context),
            new ServiceIdentityRepository(context),
            auditFactory?.Invoke(context) ?? new IdentityAuditOutbox(context),
            TimeProvider.System,
            new IdentityUnitOfWork(context),
            new ServiceIdentityClientProjection(context));
        return await handler.Handle(
            new CreateServiceIdentityCommand(actorId, workspaceId, clientId, $"create-{Guid.NewGuid():N}", "Axis Admin"),
            TestContext.Current.CancellationToken);
    }

    private async Task<Result<ServiceIdentityDto>> AddKeyAsync(
        Guid actorId,
        Guid workspaceId,
        Guid identityId,
        int revision,
        string jwk,
        string correlation,
        Func<IdentityDbContext, IServiceIdentityClientProjection>? projectionFactory = null)
    {
        await using IdentityDbContext context = database.CreateContext();
        AddServiceIdentityKeyHandler handler = new AddServiceIdentityKeyHandler(
            new WorkspaceMembershipRepository(context),
            new ServiceIdentityRepository(context),
            new IdentityAuditOutbox(context),
            TimeProvider.System,
            new IdentityUnitOfWork(context),
            projectionFactory?.Invoke(context) ?? new ServiceIdentityClientProjection(context));
        return await handler.Handle(
            new AddServiceIdentityKeyCommand(actorId, workspaceId, identityId, revision, jwk, correlation, "Axis Admin"),
            TestContext.Current.CancellationToken);
    }

    private async Task<Result<ServiceIdentityDto>> RevokeKeyAsync(
        Guid actorId,
        Guid workspaceId,
        Guid identityId,
        Guid keyId,
        int revision,
        string correlation,
        Func<IdentityDbContext, IServiceIdentityClientProjection>? projectionFactory = null)
    {
        await using IdentityDbContext context = database.CreateContext();
        RevokeServiceIdentityKeyHandler handler = new RevokeServiceIdentityKeyHandler(
            new WorkspaceMembershipRepository(context),
            new ServiceIdentityRepository(context),
            new IdentityAuditOutbox(context),
            TimeProvider.System,
            new IdentityUnitOfWork(context),
            projectionFactory?.Invoke(context) ?? new ServiceIdentityClientProjection(context));
        return await handler.Handle(
            new RevokeServiceIdentityKeyCommand(actorId, workspaceId, identityId, keyId, revision, correlation, "Axis Admin"),
            TestContext.Current.CancellationToken);
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

    private sealed class CoordinatedProjection(
        IServiceIdentityClientProjection inner,
        Barrier barrier) : IServiceIdentityClientProjection
    {
        public async Task StageAsync(ServiceIdentity identity, CancellationToken ct = default)
        {
            barrier.SignalAndWait(ct);
            await inner.StageAsync(identity, ct);
        }
    }

    private sealed class ThrowingAuditOutbox : IIdentityAuditOutbox
    {
        public Task EnqueueAsync(AuditEventV1 auditEvent, CancellationToken ct = default) =>
            throw new InvalidOperationException("Required audit is unavailable.");

        public Task<IdentityAuditOutboxEntry?> GetAsync(Guid eventId, CancellationToken ct = default) =>
            Task.FromResult<IdentityAuditOutboxEntry?>(null);
    }

    private sealed class MissingReadBackAuditOutbox(IIdentityAuditOutbox inner) : IIdentityAuditOutbox
    {
        public Task EnqueueAsync(AuditEventV1 auditEvent, CancellationToken ct = default) =>
            inner.EnqueueAsync(auditEvent, ct);

        public Task<IdentityAuditOutboxEntry?> GetAsync(Guid eventId, CancellationToken ct = default) =>
            Task.FromResult<IdentityAuditOutboxEntry?>(null);
    }
}
