using Axis.Audit.Contracts;
using Axis.Identity.Application.Commands.CreateOrganizationWorkspace;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Tests.Commands;

[Collection("IdentityDb")]
public sealed class CreateOrganizationWorkspaceIntegrationTests(IdentityDatabaseFixture database)
{
    [Fact]
    public async Task Handle_WhenSameScopedRequestRaces_ReturnsOneCanonicalGraphAndOutbox()
    {
        Guid userId = await CreateEligibleUserAsync();
        using Barrier barrier = new(2);

        Task<Result<CreateOrganizationWorkspaceDto>> first = Task.Run(() => HandleAsync(
            userId,
            "Acme",
            "same-key",
            "first-correlation",
            new CoordinatedWorkspaceSlugGenerator("acme-one", barrier)));
        Task<Result<CreateOrganizationWorkspaceDto>> second = Task.Run(() => HandleAsync(
            userId,
            "Acme",
            "same-key",
            "second-correlation",
            new CoordinatedWorkspaceSlugGenerator("acme-two", barrier)));

        Result<CreateOrganizationWorkspaceDto>[] results = await Task.WhenAll(first, second);

        results.Should().OnlyContain(result => result.IsSuccess);
        results[0].Value.OrganizationId.Should().Be(results[1].Value.OrganizationId);
        results[0].Value.WorkspaceId.Should().Be(results[1].Value.WorkspaceId);

        await using IdentityDbContext observer = database.CreateContext();
        Guid organizationId = results[0].Value.OrganizationId;
        Guid workspaceId = results[0].Value.WorkspaceId;
        Organization organization = await observer.Organizations.SingleAsync(
            x => x.Id == organizationId,
            TestContext.Current.CancellationToken);
        Workspace workspace = await observer.Workspaces.SingleAsync(
            x => x.Id == workspaceId,
            TestContext.Current.CancellationToken);
        OrganizationMembership organizationMembership = await observer.OrganizationMemberships.SingleAsync(
            x => x.OrganizationId == organizationId && x.UserId == userId,
            TestContext.Current.CancellationToken);
        WorkspaceMembership workspaceMembership = await observer.WorkspaceMemberships.SingleAsync(
            x => x.WorkspaceId == workspaceId && x.UserId == userId,
            TestContext.Current.CancellationToken);
        CreateOrganizationIdempotencyRecordEntity idempotency = await observer
            .Set<CreateOrganizationIdempotencyRecordEntity>()
            .SingleAsync(x => x.ScopedKey == CreateOrganizationIdempotencyRepository.CreateScopedKey(userId, "same-key"),
                TestContext.Current.CancellationToken);
        IdentityAuditOutboxRecord audit = await observer.Set<IdentityAuditOutboxRecord>().SingleAsync(
            x => x.TargetId == organizationId,
            TestContext.Current.CancellationToken);

        organization.Name.Should().Be("Acme");
        workspace.OrganizationId.Should().Be(organizationId);
        organizationMembership.Role.Should().Be(OrganizationMembershipRole.Owner);
        organizationMembership.Status.Should().Be(MembershipStatus.Active);
        workspaceMembership.Role.Should().Be(WorkspaceMembershipRole.Administrator);
        workspaceMembership.Status.Should().Be(MembershipStatus.Active);
        idempotency.CanonicalRequest.Should().Be("Acme");
        idempotency.OrganizationId.Should().Be(organizationId);
        idempotency.WorkspaceId.Should().Be(workspaceId);
        audit.Action.Should().Be("organization.workspace.created");
        audit.EventId.Should().Be(organizationId);
        audit.TargetType.Should().Be("Organization");
        audit.ActorId.Should().Be(userId);
        audit.SubjectId.Should().Be(userId);
        audit.WorkspaceId.Should().Be(workspaceId);
        audit.Status.Should().Be(IdentityAuditOutboxStatus.Pending);
        audit.AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenScopedKeyIsReusedWithDifferentPayload_ConflictsWithoutSecondGraph()
    {
        Guid userId = await CreateEligibleUserAsync();

        Result<CreateOrganizationWorkspaceDto> original = await HandleAsync(
            userId,
            "Acme",
            "reused-key",
            "original-correlation");
        Result<CreateOrganizationWorkspaceDto> conflict = await HandleAsync(
            userId,
            "Other",
            "reused-key",
            "conflict-correlation");

        original.IsSuccess.Should().BeTrue();
        conflict.IsFailure.Should().BeTrue();
        conflict.ErrorCode.Should().Be(ErrorCodes.Conflict);

        await using IdentityDbContext observer = database.CreateContext();
        (await observer.Organizations.CountAsync(
            x => x.Id == original.Value.OrganizationId,
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await observer.Workspaces.CountAsync(
            x => x.Id == original.Value.WorkspaceId,
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await observer.OrganizationMemberships.CountAsync(
            x => x.UserId == userId,
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await observer.WorkspaceMemberships.CountAsync(
            x => x.WorkspaceId == original.Value.WorkspaceId && x.UserId == userId,
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await observer.Set<CreateOrganizationIdempotencyRecordEntity>().CountAsync(
            x => x.ScopedKey == CreateOrganizationIdempotencyRepository.CreateScopedKey(userId, "reused-key"),
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await observer.Set<IdentityAuditOutboxRecord>().CountAsync(
            x => x.TargetId == original.Value.OrganizationId,
            TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenWorkspaceSlugConflicts_RollsBackTheIncompleteGraph()
    {
        Guid userId = await CreateEligibleUserAsync();
        const string collidingSlug = "occupied-workspace";
        await SeedOrganizationWorkspaceAsync(collidingSlug);

        Result<CreateOrganizationWorkspaceDto> result = await HandleAsync(
            userId,
            "Uncommitted Organization",
            "failing-key",
            "failure-correlation",
            new FixedWorkspaceSlugGenerator(collidingSlug));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);

        await using IdentityDbContext observer = database.CreateContext();
        (await observer.Organizations.CountAsync(
            x => x.Name == "Uncommitted Organization",
            TestContext.Current.CancellationToken)).Should().Be(0);
        (await observer.OrganizationMemberships.CountAsync(
            x => x.UserId == userId,
            TestContext.Current.CancellationToken)).Should().Be(0);
        (await observer.WorkspaceMemberships.CountAsync(
            x => x.UserId == userId && x.Role == WorkspaceMembershipRole.Administrator,
            TestContext.Current.CancellationToken)).Should().Be(0);
        (await observer.Set<CreateOrganizationIdempotencyRecordEntity>().CountAsync(
            x => x.ScopedKey == CreateOrganizationIdempotencyRepository.CreateScopedKey(userId, "failing-key"),
            TestContext.Current.CancellationToken)).Should().Be(0);
        (await observer.Set<IdentityAuditOutboxRecord>().CountAsync(
            x => x.ActorId == userId && x.Action == "organization.workspace.created",
            TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenRequiredAuditCannotBeEnqueued_PersistsNothing()
    {
        Guid userId = await CreateEligibleUserAsync();

        Func<Task> act = async () => await HandleAsync(
            userId,
            "Unaudited Organization",
            "audit-failure-key",
            "audit-failure-correlation",
            auditFactory: _ => new ThrowingAuditOutbox());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Required audit is unavailable.");

        await using IdentityDbContext observer = database.CreateContext();
        (await observer.Organizations.CountAsync(
            x => x.Name == "Unaudited Organization",
            TestContext.Current.CancellationToken)).Should().Be(0);
        (await observer.OrganizationMemberships.CountAsync(
            x => x.UserId == userId,
            TestContext.Current.CancellationToken)).Should().Be(0);
        (await observer.WorkspaceMemberships.CountAsync(
            x => x.UserId == userId && x.Role == WorkspaceMembershipRole.Administrator,
            TestContext.Current.CancellationToken)).Should().Be(0);
        (await observer.Set<CreateOrganizationIdempotencyRecordEntity>().CountAsync(
            x => x.ScopedKey == CreateOrganizationIdempotencyRepository.CreateScopedKey(
                userId,
                "audit-failure-key"),
            TestContext.Current.CancellationToken)).Should().Be(0);
        (await observer.Set<IdentityAuditOutboxRecord>().CountAsync(
            x => x.ActorId == userId && x.Action == "organization.workspace.created",
            TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenReadBackFailsAfterCommit_CanonicalRetryRecoversTheCommittedOutcome()
    {
        Guid userId = await CreateEligibleUserAsync();

        Result<CreateOrganizationWorkspaceDto> uncertain = await HandleAsync(
            userId,
            "Recoverable Organization",
            "read-back-key",
            "first-correlation",
            auditFactory: context => new MissingReadBackAuditOutbox(new IdentityAuditOutbox(context)));
        Result<CreateOrganizationWorkspaceDto> recovered = await HandleAsync(
            userId,
            "Recoverable Organization",
            "read-back-key",
            "retry-correlation");

        uncertain.IsFailure.Should().BeTrue();
        uncertain.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        recovered.IsSuccess.Should().BeTrue();

        await using IdentityDbContext observer = database.CreateContext();
        (await observer.Organizations.CountAsync(
            x => x.Id == recovered.Value.OrganizationId,
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await observer.Workspaces.CountAsync(
            x => x.Id == recovered.Value.WorkspaceId,
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await observer.Set<CreateOrganizationIdempotencyRecordEntity>().CountAsync(
            x => x.ScopedKey == CreateOrganizationIdempotencyRepository.CreateScopedKey(
                userId,
                "read-back-key"),
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await observer.Set<IdentityAuditOutboxRecord>().CountAsync(
            x => x.EventId == recovered.Value.OrganizationId,
            TestContext.Current.CancellationToken)).Should().Be(1);
    }

    private async Task<Guid> CreateEligibleUserAsync()
    {
        User user = User.Create(
            "Integration User",
            Email.Create($"create-organization-{Guid.NewGuid():N}@example.com").Value);
        user.VerifyEmail();
        Workspace personalWorkspace = Workspace.CreatePersonal(
            "Integration Workspace",
            WorkspaceSlug.Create($"create-organization-{Guid.NewGuid():N}").Value);
        personalWorkspace.ActivateAfterOwnerVerification();
        WorkspaceMembership personalOwner = WorkspaceMembership.CreatePersonalOwner(
            personalWorkspace.Id,
            user.Id);
        await using IdentityDbContext context = database.CreateContext();
        context.Users.Add(user);
        context.Workspaces.Add(personalWorkspace);
        context.WorkspaceMemberships.Add(personalOwner);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user.Id;
    }

    private async Task SeedOrganizationWorkspaceAsync(string slug)
    {
        Organization organization = Organization.Create("Existing Organization");
        Workspace workspace = Workspace.CreateOrganization(
            "Existing Workspace",
            WorkspaceSlug.Create(slug).Value,
            organization.Id);
        await using IdentityDbContext context = database.CreateContext();
        context.Organizations.Add(organization);
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Result<CreateOrganizationWorkspaceDto>> HandleAsync(
        Guid userId,
        string name,
        string key,
        string correlationId,
        IWorkspaceSlugGenerator? slugs = null,
        Func<IdentityDbContext, IIdentityAuditOutbox>? auditFactory = null)
    {
        await using IdentityDbContext context = database.CreateContext();
        WorkspaceRepository workspaces = new(context);
        CreateOrganizationWorkspaceHandler handler = new(
            new UserRepository(context),
            new OrganizationRepository(context),
            new OrganizationMembershipRepository(context),
            workspaces,
            new WorkspaceMembershipRepository(context),
            new CreateOrganizationIdempotencyRepository(context),
            slugs ?? new WorkspaceSlugGenerator(workspaces),
            auditFactory?.Invoke(context) ?? new IdentityAuditOutbox(context),
            new IdentityUnitOfWork(context));

        return await handler.Handle(
            new CreateOrganizationWorkspaceCommand(userId, name, key, correlationId),
            TestContext.Current.CancellationToken);
    }

    private sealed class CoordinatedWorkspaceSlugGenerator(string slug, Barrier barrier) : IWorkspaceSlugGenerator
    {
        public string GenerateBaseSlug(string workspaceName) => slug;

        public Task<WorkspaceSlug> GenerateUniqueSlugAsync(string workspaceName, CancellationToken cancellationToken)
        {
            barrier.SignalAndWait(cancellationToken);
            return Task.FromResult(WorkspaceSlug.Create(slug).Value);
        }
    }

    private sealed class FixedWorkspaceSlugGenerator(string slug) : IWorkspaceSlugGenerator
    {
        public string GenerateBaseSlug(string workspaceName) => slug;

        public Task<WorkspaceSlug> GenerateUniqueSlugAsync(string workspaceName, CancellationToken cancellationToken) =>
            Task.FromResult(WorkspaceSlug.Create(slug).Value);
    }

    private sealed class ThrowingAuditOutbox : IIdentityAuditOutbox
    {
        public Task EnqueueAsync(AuditEventV1 auditEvent, CancellationToken ct = default) =>
            throw new InvalidOperationException("Required audit is unavailable.");

        public Task<IdentityAuditOutboxEntry?> GetAsync(
            Guid eventId,
            CancellationToken ct = default) =>
            Task.FromResult<IdentityAuditOutboxEntry?>(null);
    }

    private sealed class MissingReadBackAuditOutbox(IIdentityAuditOutbox inner) : IIdentityAuditOutbox
    {
        public Task EnqueueAsync(AuditEventV1 auditEvent, CancellationToken ct = default) =>
            inner.EnqueueAsync(auditEvent, ct);

        public Task<IdentityAuditOutboxEntry?> GetAsync(
            Guid eventId,
            CancellationToken ct = default) =>
            Task.FromResult<IdentityAuditOutboxEntry?>(null);
    }
}
