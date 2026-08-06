using System.Collections.Concurrent;
using Axis.Identity.Application;
using Axis.Identity.Application.Commands.AcceptWorkspaceInvitation;
using Axis.Identity.Application.Commands.ExchangeWorkspaceInvitation;
using Axis.Identity.Application.Commands.InviteWorkspaceMember;
using Axis.Identity.Application.Commands.RevokeWorkspaceInvitation;
using Axis.Identity.Application.Repositories;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.Identity.Infrastructure.Tests.Commands;

[Collection("IdentityDb")]
public sealed class WorkspaceInvitationIntegrationTests(IdentityDatabaseFixture database)
{
    [Fact]
    public async Task AT005_WhenRevokeRacesExchange_ExactlyOneLifecycleOutcomeCommits()
    {
        (Guid administratorId, Guid workspaceId, Guid invitationId, string rawToken) =
            await SeedPendingInvitationAsync();
        using Barrier barrier = new(2);

        Task<Result<WorkspaceInvitationLifecycleDto>> revoke = Task.Run(() => RevokeAsync(
            administratorId,
            workspaceId,
            invitationId,
            barrier));
        Task<Result<WorkspaceInvitationExchangeDto>> exchange = Task.Run(() => ExchangeAsync(
            rawToken,
            barrier));

        await Task.WhenAll(revoke, exchange);
        Result<WorkspaceInvitationLifecycleDto> revokeResult = await revoke;
        Result<WorkspaceInvitationExchangeDto> exchangeResult = await exchange;

        new[] { revokeResult.IsSuccess, exchangeResult.IsSuccess }
            .Count(success => success)
            .Should().Be(1);

        await using IdentityDbContext observer = database.CreateContext();
        WorkspaceInvitation persisted = await observer.WorkspaceInvitations
            .Include(invitation => invitation.TokenGenerations)
            .Include(invitation => invitation.Handoffs)
            .SingleAsync(
                invitation => invitation.Id == invitationId,
                TestContext.Current.CancellationToken);
        if (revokeResult.IsSuccess)
        {
            persisted.Status.Should().Be(WorkspaceInvitationStatus.Revoked);
            persisted.CurrentToken.Status.Should().Be(InvitationTokenStatus.Revoked);
            persisted.Handoffs.Should().BeEmpty();
        }
        else
        {
            persisted.Status.Should().Be(WorkspaceInvitationStatus.Pending);
            persisted.CurrentToken.Status.Should().Be(InvitationTokenStatus.Exchanged);
            persisted.Handoffs.Should().ContainSingle(handoff =>
                handoff.Status == InvitationHandoffStatus.Active);
        }

        List<IdentityAuditOutboxRecord> audits = await observer.Set<IdentityAuditOutboxRecord>()
            .Where(row => row.TargetId == invitationId)
            .ToListAsync(TestContext.Current.CancellationToken);
        audits.Should().ContainSingle(row =>
            row.Action == (revokeResult.IsSuccess
                ? "workspace.invitation.revoked"
                : "workspace.invitation.exchanged"));
    }

    [Fact]
    public async Task AT004_AmbiguousDeliveryRetriesSameGenerationAndAcceptanceLink()
    {
        (Guid _, Guid workspaceId) = await SeedAdministratorAsync();
        DateTimeOffset current = DateTimeOffset.UtcNow;
        string rawToken = OpaqueTokenGenerator.Create().RawToken;
        InvitationDeliveryMessage message;

        await using (IdentityDbContext seed = database.CreateContext())
        {
            Workspace workspace = await seed.Workspaces.SingleAsync(
                candidate => candidate.Id == workspaceId,
                TestContext.Current.CancellationToken);
            User inviter = await seed.Users.SingleAsync(
                candidate => seed.WorkspaceMemberships.Any(membership =>
                    membership.WorkspaceId == workspaceId && membership.UserId == candidate.Id),
                TestContext.Current.CancellationToken);
            WorkspaceInvitation invitation = WorkspaceInvitation.Create(
                workspace.OrganizationId!.Value,
                workspaceId,
                inviter.Id,
                "delivery-recipient@example.com",
                WorkspaceMembershipRole.Member,
                current.UtcDateTime,
                current.UtcDateTime.AddDays(7),
                OpaqueTokenGenerator.Hash(rawToken),
                "protected:delivery",
                "stable-delivery-correlation");
            message = new InvitationDeliveryMessage(
                invitation.Id,
                1,
                "delivery-recipient@example.com",
                rawToken,
                "Invitation Organization",
                "Invitation Workspace",
                inviter.FullName,
                "Member",
                current.UtcDateTime.AddDays(7),
                "en",
                "stable-delivery-correlation");
            seed.WorkspaceInvitations.Add(invitation);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        AmbiguousEmailSender emailSender = new();
        using ServiceProvider services = CreateDeliveryServices(message, emailSender);
        WorkspaceInvitationDeliveryDispatcher firstAttempt = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(current),
            NullLogger<WorkspaceInvitationDeliveryDispatcher>.Instance);
        await firstAttempt.DispatchBatch(TestContext.Current.CancellationToken);

        WorkspaceInvitationDeliveryDispatcher retry = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(current.AddMinutes(1)),
            NullLogger<WorkspaceInvitationDeliveryDispatcher>.Instance);
        await retry.DispatchBatch(TestContext.Current.CancellationToken);

        emailSender.Messages.Should().HaveCount(2);
        emailSender.Messages.Should().OnlyContain(delivery =>
            delivery.Generation == 1
            && delivery.RawToken == rawToken
            && delivery.DeliveryCorrelation == "stable-delivery-correlation");

        await using IdentityDbContext observer = database.CreateContext();
        WorkspaceInvitation persisted = await observer.WorkspaceInvitations
            .Include(invitation => invitation.TokenGenerations)
            .Include(invitation => invitation.Handoffs)
            .SingleAsync(
                invitation => invitation.Id == message.InvitationId,
                TestContext.Current.CancellationToken);
        persisted.TokenGenerations.Should().ContainSingle();
        persisted.CurrentToken.Generation.Should().Be(1);
        persisted.CurrentToken.TokenHash.Should().Be(OpaqueTokenGenerator.Hash(rawToken));
        persisted.CurrentToken.DeliveryStatus.Should().Be(InvitationDeliveryStatus.Delivered);
        persisted.CurrentToken.DeliveryEnvelope.Should().BeNull();
        (await observer.Set<IdentityAuditOutboxRecord>().CountAsync(
            row => row.TargetId == persisted.Id
                && row.Action == "workspace.invitation.delivery"
                && row.Outcome == "succeeded",
            TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task AT007_LifecycleExpiresThenPurgesRecipientOnlyAfterAuditDelivery()
    {
        (Guid _, Guid workspaceId) = await SeedAdministratorAsync();
        DateTimeOffset current = DateTimeOffset.UtcNow;
        Guid invitationId;

        await using (IdentityDbContext seed = database.CreateContext())
        {
            Workspace workspace = await seed.Workspaces.SingleAsync(
                candidate => candidate.Id == workspaceId,
                TestContext.Current.CancellationToken);
            Guid inviterId = await seed.WorkspaceMemberships
                .Where(membership => membership.WorkspaceId == workspaceId)
                .Select(membership => membership.UserId)
                .SingleAsync(TestContext.Current.CancellationToken);
            WorkspaceInvitation invitation = WorkspaceInvitation.Create(
                workspace.OrganizationId!.Value,
                workspaceId,
                inviterId,
                "expired-recipient@example.com",
                WorkspaceMembershipRole.Member,
                current.UtcDateTime.AddDays(-8),
                current.UtcDateTime.AddDays(-1),
                OpaqueTokenGenerator.Create().TokenHash,
                "protected-envelope",
                "expiry-delivery");
            invitationId = invitation.Id;
            seed.WorkspaceInvitations.Add(invitation);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using ServiceProvider services = CreateLifecycleServices(current);
        WorkspaceInvitationLifecycleWorker worker = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(current),
            NullLogger<WorkspaceInvitationLifecycleWorker>.Instance);

        await worker.ProcessBatch(TestContext.Current.CancellationToken);

        await using (IdentityDbContext pendingAudit = database.CreateContext())
        {
            WorkspaceInvitation expired = await pendingAudit.WorkspaceInvitations
                .Include(invitation => invitation.TokenGenerations)
                .Include(invitation => invitation.Handoffs)
                .SingleAsync(
                    invitation => invitation.Id == invitationId,
                    TestContext.Current.CancellationToken);
            expired.Status.Should().Be(WorkspaceInvitationStatus.Expired);
            expired.NormalizedEmail.Should().Be("expired-recipient@example.com");
            expired.CurrentToken.DeliveryEnvelope.Should().BeNull();
            expired.TerminalMaterialPurgedAt.Should().BeNull();

            IdentityAuditOutboxRecord audit = await pendingAudit.Set<IdentityAuditOutboxRecord>()
                .SingleAsync(
                    row => row.TargetId == invitationId
                        && row.Action == "workspace.invitation.expired",
                    TestContext.Current.CancellationToken);
            audit.Status.Should().Be(IdentityAuditOutboxStatus.Pending);
            audit.Status = IdentityAuditOutboxStatus.Delivered;
            await pendingAudit.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await worker.ProcessBatch(TestContext.Current.CancellationToken);

        await using IdentityDbContext observer = database.CreateContext();
        WorkspaceInvitation purged = await observer.WorkspaceInvitations
            .Include(invitation => invitation.TokenGenerations)
            .Include(invitation => invitation.Handoffs)
            .SingleAsync(
                invitation => invitation.Id == invitationId,
                TestContext.Current.CancellationToken);
        purged.Status.Should().Be(WorkspaceInvitationStatus.Expired);
        purged.NormalizedEmail.Should().BeNull();
        purged.TerminalMaterialPurgedAt.Should().BeCloseTo(
            current.UtcDateTime,
            TimeSpan.FromMilliseconds(1));
        purged.CurrentToken.TokenHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AT005_WhenAcceptanceRaces_PersistsOneMembershipResultAndCanonicalReplayAudit()
    {
        (Guid recipientId, Guid organizationId, Guid workspaceId, string handoffHash) =
            await SeedAcceptanceAsync();
        using Barrier barrier = new(2);

        Task<Result<WorkspaceInvitationAcceptanceDto>> first = Task.Run(() => AcceptAsync(
            recipientId,
            handoffHash,
            "first-acceptance",
            barrier));
        Task<Result<WorkspaceInvitationAcceptanceDto>> second = Task.Run(() => AcceptAsync(
            recipientId,
            handoffHash,
            "second-acceptance",
            barrier));

        Result<WorkspaceInvitationAcceptanceDto>[] results = await Task.WhenAll(first, second);

        results.Should().ContainSingle(result =>
            result.IsSuccess && result.Value.Outcome == "Accepted");
        results.Should().ContainSingle(result =>
            result.IsFailure && result.ProblemCode == IdentityProblemCodes.InvitationAccessInvalid);

        await using IdentityDbContext observer = database.CreateContext();
        (await observer.OrganizationMemberships.CountAsync(
            membership => membership.OrganizationId == organizationId
                && membership.UserId == recipientId
                && membership.Status == MembershipStatus.Active,
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await observer.WorkspaceMemberships.CountAsync(
            membership => membership.WorkspaceId == workspaceId
                && membership.UserId == recipientId
                && membership.Status == MembershipStatus.Active,
            TestContext.Current.CancellationToken)).Should().Be(1);

        WorkspaceInvitation invitation = await observer.WorkspaceInvitations
            .Include(row => row.TokenGenerations)
            .Include(row => row.Handoffs)
            .SingleAsync(
                row => row.WorkspaceId == workspaceId,
                TestContext.Current.CancellationToken);
        invitation.Status.Should().Be(WorkspaceInvitationStatus.Accepted);
        invitation.Handoffs.Should().ContainSingle(handoff =>
            handoff.Status == InvitationHandoffStatus.Accepted);

        List<IdentityAuditOutboxRecord> audits = await observer.Set<IdentityAuditOutboxRecord>()
            .Where(row => row.TargetId == invitation.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        audits.Should().ContainSingle(row =>
            row.Action == "workspace.invitation.accepted" && row.Outcome == "succeeded");
        audits.Should().ContainSingle(row =>
            row.Action == "workspace.invitation.accept_rejected" && row.Outcome == "used");
    }

    [Fact]
    public async Task AT002_WhenEquivalentCreatesRace_PersistsOneInvitationTokenDeliveryAndAudit()
    {
        (Guid userId, Guid workspaceId) = await SeedAdministratorAsync();
        using Barrier barrier = new(2);
        CapturingEnvelopeProtector envelopes = new();
        CoordinatedRateLimiter rateLimiter = new(barrier);

        Task<Result<InviteWorkspaceMemberDto>> first = Task.Run(() => HandleAsync(
            userId,
            workspaceId,
            "recipient@example.com",
            "Member",
            "first-correlation",
            rateLimiter,
            envelopes));
        Task<Result<InviteWorkspaceMemberDto>> second = Task.Run(() => HandleAsync(
            userId,
            workspaceId,
            "RECIPIENT@example.com",
            "Member",
            "second-correlation",
            rateLimiter,
            envelopes));

        Result<InviteWorkspaceMemberDto>[] results = await Task.WhenAll(first, second);

        results.Should().OnlyContain(result => result.IsSuccess);
        results.Select(result => result.Value.Invitation!.InvitationId).Distinct().Should().ContainSingle();
        results.Select(result => result.Value.Outcome).Should().Contain("Created");
        results.Select(result => result.Value.Outcome).Should().Contain("CanonicalPending");

        await using IdentityDbContext observer = database.CreateContext();
        WorkspaceInvitation invitation = await observer.WorkspaceInvitations
            .Include(row => row.TokenGenerations)
            .Include(row => row.Handoffs)
            .SingleAsync(
                row => row.WorkspaceId == workspaceId
                    && row.NormalizedEmail == "recipient@example.com",
                TestContext.Current.CancellationToken);
        IdentityAuditOutboxRecord audit = await observer.Set<IdentityAuditOutboxRecord>()
            .SingleAsync(
                row => row.TargetId == invitation.Id
                    && row.Action == "workspace.invitation.created",
                TestContext.Current.CancellationToken);

        invitation.Status.Should().Be(WorkspaceInvitationStatus.Pending);
        invitation.TokenGenerations.Should().ContainSingle();
        invitation.CurrentToken.DeliveryStatus.Should().Be(InvitationDeliveryStatus.Pending);
        invitation.CurrentToken.DeliveryEnvelope.Should().StartWith("protected:");
        envelopes.Messages.Should().HaveCount(2);
        envelopes.Messages.Should().OnlyContain(message =>
            message.RawToken.Length > 0
            && !invitation.CurrentToken.DeliveryEnvelope!.Contains(message.RawToken));
        audit.ActorId.Should().Be(userId);
        audit.SubjectId.Should().Be(userId);
        audit.WorkspaceId.Should().Be(workspaceId);
        audit.MetadataJson.Should().NotContain("recipient@example.com");
        audit.MetadataJson.ToLowerInvariant().Should().NotContain("token");
    }

    private async Task<(Guid UserId, Guid WorkspaceId)> SeedAdministratorAsync()
    {
        User user = User.Create(
            "Invitation Administrator",
            Email.Create($"invite-admin-{Guid.NewGuid():N}@example.com").Value);
        user.VerifyEmail();
        Organization organization = Organization.Create("Invitation Organization");
        Workspace workspace = Workspace.CreateOrganization(
            "Invitation Workspace",
            WorkspaceSlug.Create($"invitation-{Guid.NewGuid():N}").Value,
            organization.Id);
        OrganizationMembership organizationMembership = OrganizationMembership.Create(
            organization.Id,
            user.Id,
            OrganizationMembershipRole.Administrator);
        WorkspaceMembership workspaceMembership = WorkspaceMembership.CreateOrganizationMember(
            workspace.Id,
            user.Id,
            WorkspaceMembershipRole.Administrator);

        await using IdentityDbContext context = database.CreateContext();
        context.Users.Add(user);
        context.Organizations.Add(organization);
        context.Workspaces.Add(workspace);
        context.OrganizationMemberships.Add(organizationMembership);
        context.WorkspaceMemberships.Add(workspaceMembership);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (user.Id, workspace.Id);
    }

    private async Task<(Guid RecipientId, Guid OrganizationId, Guid WorkspaceId, string HandoffHash)>
        SeedAcceptanceAsync()
    {
        (Guid _, Guid workspaceId) = await SeedAdministratorAsync();
        string recipientEmail = $"invite-recipient-{Guid.NewGuid():N}@example.com";
        User recipient = User.Create("Invitation Recipient", Email.Create(recipientEmail).Value);
        recipient.VerifyEmail();
        string tokenHash = OpaqueTokenGenerator.Create().TokenHash;
        string handoffHash = OpaqueTokenGenerator.Create().TokenHash;
        DateTime now = DateTime.UtcNow;

        await using IdentityDbContext context = database.CreateContext();
        Workspace workspace = await context.Workspaces.SingleAsync(
            candidate => candidate.Id == workspaceId,
            TestContext.Current.CancellationToken);
        WorkspaceInvitation invitation = WorkspaceInvitation.Create(
            workspace.OrganizationId!.Value,
            workspace.Id,
            (await context.WorkspaceMemberships.SingleAsync(
                membership => membership.WorkspaceId == workspaceId,
                TestContext.Current.CancellationToken)).UserId,
            recipientEmail,
            WorkspaceMembershipRole.Member,
            now,
            now.AddDays(7),
            tokenHash,
            "protected-envelope",
            "acceptance-delivery");
        invitation.Exchange(
                tokenHash,
                handoffHash,
                now.AddHours(2),
                now,
                invitation.Revision)
            .Should().Be(InvitationExchangeOutcome.Exchanged);

        context.Users.Add(recipient);
        context.WorkspaceInvitations.Add(invitation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (recipient.Id, workspace.OrganizationId.Value, workspaceId, handoffHash);
    }

    private async Task<(Guid AdministratorId, Guid WorkspaceId, Guid InvitationId, string RawToken)>
        SeedPendingInvitationAsync()
    {
        (Guid administratorId, Guid workspaceId) = await SeedAdministratorAsync();
        (string rawToken, string tokenHash) = OpaqueTokenGenerator.Create();
        DateTime now = DateTime.UtcNow;

        await using IdentityDbContext context = database.CreateContext();
        Workspace workspace = await context.Workspaces.SingleAsync(
            candidate => candidate.Id == workspaceId,
            TestContext.Current.CancellationToken);
        WorkspaceInvitation invitation = WorkspaceInvitation.Create(
            workspace.OrganizationId!.Value,
            workspaceId,
            administratorId,
            "race-recipient@example.com",
            WorkspaceMembershipRole.Member,
            now,
            now.AddDays(7),
            tokenHash,
            "protected-race-envelope",
            "race-delivery");
        context.WorkspaceInvitations.Add(invitation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (administratorId, workspaceId, invitation.Id, rawToken);
    }

    private async Task<Result<WorkspaceInvitationLifecycleDto>> RevokeAsync(
        Guid administratorId,
        Guid workspaceId,
        Guid invitationId,
        Barrier barrier)
    {
        await using IdentityDbContext context = database.CreateContext();
        RevokeWorkspaceInvitationHandler handler = new(
            new OrganizationMembershipRepository(context),
            new WorkspaceMembershipRepository(context),
            new FirstLookupCoordinatedInvitationRepository(
                new WorkspaceInvitationRepository(context),
                barrier),
            new IdentityAuditOutbox(context),
            TimeProvider.System,
            new IdentityUnitOfWork(context));
        return await handler.Handle(
            new RevokeWorkspaceInvitationCommand(
                administratorId,
                workspaceId,
                invitationId,
                1,
                "revoke-race"),
            TestContext.Current.CancellationToken);
    }

    private async Task<Result<WorkspaceInvitationExchangeDto>> ExchangeAsync(
        string rawToken,
        Barrier barrier)
    {
        await using IdentityDbContext context = database.CreateContext();
        ExchangeWorkspaceInvitationHandler handler = new(
            new FirstLookupCoordinatedInvitationRepository(
                new WorkspaceInvitationRepository(context),
                barrier),
            new PassThroughRateLimiter(),
            new IdentityAuditOutbox(context),
            new WorkspaceInvitationPolicy(TimeSpan.FromDays(7), TimeSpan.FromHours(2), 20, 100),
            TimeProvider.System,
            new IdentityUnitOfWork(context));
        return await handler.Handle(
            new ExchangeWorkspaceInvitationCommand(rawToken, "test-partition", "exchange-race"),
            TestContext.Current.CancellationToken);
    }

    private async Task<Result<WorkspaceInvitationAcceptanceDto>> AcceptAsync(
        Guid recipientId,
        string handoffHash,
        string correlationId,
        Barrier barrier)
    {
        await using IdentityDbContext context = database.CreateContext();
        IWorkspaceInvitationRepository invitations = new CoordinatedInvitationRepository(
            new WorkspaceInvitationRepository(context),
            barrier);
        AcceptWorkspaceInvitationHandler handler = new(
            new UserRepository(context),
            new OrganizationRepository(context),
            new OrganizationMembershipRepository(context),
            new WorkspaceRepository(context),
            new WorkspaceMembershipRepository(context),
            invitations,
            new IdentityAuditOutbox(context),
            TimeProvider.System,
            new IdentityUnitOfWork(context));

        return await handler.Handle(
            new AcceptWorkspaceInvitationCommand(handoffHash, recipientId, correlationId),
            TestContext.Current.CancellationToken);
    }

    private async Task<Result<InviteWorkspaceMemberDto>> HandleAsync(
        Guid userId,
        Guid workspaceId,
        string email,
        string role,
        string correlation,
        IWorkspaceInvitationRateLimiter rateLimiter,
        IInvitationDeliveryEnvelopeProtector envelopes)
    {
        await using IdentityDbContext context = database.CreateContext();
        InviteWorkspaceMemberHandler handler = new(
            new UserRepository(context),
            new OrganizationRepository(context),
            new OrganizationMembershipRepository(context),
            new WorkspaceRepository(context),
            new WorkspaceMembershipRepository(context),
            new WorkspaceInvitationRepository(context),
            rateLimiter,
            envelopes,
            new IdentityAuditOutbox(context),
            new WorkspaceInvitationPolicy(
                TimeSpan.FromDays(7),
                TimeSpan.FromHours(2),
                20,
                100),
            TimeProvider.System,
            new IdentityUnitOfWork(context));
        return await handler.Handle(
            new InviteWorkspaceMemberCommand(userId, workspaceId, email, role, correlation),
            TestContext.Current.CancellationToken);
    }

    private ServiceProvider CreateLifecycleServices(DateTimeOffset current)
    {
        ServiceCollection services = new();
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(database.ConnectionString).UseOpenIddict());
        services.AddScoped<IWorkspaceInvitationRepository, WorkspaceInvitationRepository>();
        services.AddScoped<IIdentityAuditOutbox, IdentityAuditOutbox>();
        services.AddScoped<IUnitOfWork, IdentityUnitOfWork>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(current));
        return services.BuildServiceProvider();
    }

    private ServiceProvider CreateDeliveryServices(
        InvitationDeliveryMessage message,
        AmbiguousEmailSender emailSender)
    {
        ServiceCollection services = new();
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(database.ConnectionString).UseOpenIddict());
        services.AddScoped<IWorkspaceInvitationRepository, WorkspaceInvitationRepository>();
        services.AddScoped<IIdentityAuditOutbox, IdentityAuditOutbox>();
        services.AddScoped<IUnitOfWork, IdentityUnitOfWork>();
        services.AddSingleton<IInvitationDeliveryEnvelopeProtector>(
            new StaticEnvelopeProtector(message));
        services.AddSingleton<IEmailSender>(emailSender);
        return services.BuildServiceProvider();
    }

    private sealed class CoordinatedRateLimiter(Barrier barrier) : IWorkspaceInvitationRateLimiter
    {
        public Task<Result> AcquireCreateAsync(
            Guid inviterUserId,
            Guid workspaceId,
            string normalizedEmail,
            CancellationToken ct = default)
        {
            barrier.SignalAndWait(ct);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> AcquireResendAsync(
            Guid inviterUserId,
            Guid invitationId,
            CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> AcquireExchangeAsync(
            string requestPartition,
            string tokenHash,
            CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class CapturingEnvelopeProtector : IInvitationDeliveryEnvelopeProtector
    {
        private readonly ConcurrentBag<InvitationDeliveryMessage> messages = [];

        public IReadOnlyCollection<InvitationDeliveryMessage> Messages => messages;

        public string Protect(InvitationDeliveryMessage message)
        {
            messages.Add(message);
            return $"protected:{OpaqueTokenGenerator.Hash(message.RawToken)}";
        }

        public InvitationDeliveryMessage Unprotect(string protectedEnvelope) =>
            throw new NotSupportedException();
    }

    private sealed class CoordinatedInvitationRepository(
        IWorkspaceInvitationRepository inner,
        Barrier barrier) : IWorkspaceInvitationRepository
    {
        private int coordinated;

        public Task AddAsync(WorkspaceInvitation invitation, CancellationToken ct = default) =>
            inner.AddAsync(invitation, ct);

        public Task<WorkspaceInvitation?> GetByIdAsync(
            Guid workspaceId,
            Guid invitationId,
            CancellationToken ct = default) =>
            inner.GetByIdAsync(workspaceId, invitationId, ct);

        public Task<WorkspaceInvitation?> GetCanonicalPendingAsync(
            Guid workspaceId,
            string normalizedEmail,
            WorkspaceMembershipRole role,
            CancellationToken ct = default) =>
            inner.GetCanonicalPendingAsync(workspaceId, normalizedEmail, role, ct);

        public Task<WorkspaceInvitation?> GetByTokenHashAsync(
            string tokenHash,
            CancellationToken ct = default) =>
            inner.GetByTokenHashAsync(tokenHash, ct);

        public async Task<WorkspaceInvitation?> GetByHandoffHashAsync(
            string handoffHash,
            CancellationToken ct = default)
        {
            WorkspaceInvitation? invitation = await inner.GetByHandoffHashAsync(handoffHash, ct);
            if (Interlocked.Exchange(ref coordinated, 1) == 0)
                barrier.SignalAndWait(ct);
            return invitation;
        }

        public Task<IReadOnlyList<WorkspaceInvitation>> ListForWorkspaceAsync(
            Guid workspaceId,
            int offset,
            int limit,
            CancellationToken ct = default) =>
            inner.ListForWorkspaceAsync(workspaceId, offset, limit, ct);

        public Task<int> CountForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) =>
            inner.CountForWorkspaceAsync(workspaceId, ct);

        public Task<IReadOnlyList<WorkspaceInvitation>> ListDueDeliveryAsync(
            DateTime now,
            int limit,
            CancellationToken ct = default) =>
            inner.ListDueDeliveryAsync(now, limit, ct);

        public Task<IReadOnlyList<WorkspaceInvitation>> ListDueExpiryAsync(
            DateTime now,
            int limit,
            CancellationToken ct = default) =>
            inner.ListDueExpiryAsync(now, limit, ct);

        public Task<IReadOnlyList<WorkspaceInvitation>> ListReadyForTerminalCleanupAsync(
            int limit,
            CancellationToken ct = default) =>
            inner.ListReadyForTerminalCleanupAsync(limit, ct);
    }

    private sealed class FirstLookupCoordinatedInvitationRepository(
        IWorkspaceInvitationRepository inner,
        Barrier barrier) : IWorkspaceInvitationRepository
    {
        private int coordinated;

        private void Coordinate(CancellationToken ct)
        {
            if (Interlocked.Exchange(ref coordinated, 1) == 0)
                barrier.SignalAndWait(ct);
        }

        public Task AddAsync(WorkspaceInvitation invitation, CancellationToken ct = default) =>
            inner.AddAsync(invitation, ct);

        public async Task<WorkspaceInvitation?> GetByIdAsync(
            Guid workspaceId,
            Guid invitationId,
            CancellationToken ct = default)
        {
            WorkspaceInvitation? invitation = await inner.GetByIdAsync(workspaceId, invitationId, ct);
            Coordinate(ct);
            return invitation;
        }

        public Task<WorkspaceInvitation?> GetCanonicalPendingAsync(
            Guid workspaceId,
            string normalizedEmail,
            WorkspaceMembershipRole role,
            CancellationToken ct = default) =>
            inner.GetCanonicalPendingAsync(workspaceId, normalizedEmail, role, ct);

        public async Task<WorkspaceInvitation?> GetByTokenHashAsync(
            string tokenHash,
            CancellationToken ct = default)
        {
            WorkspaceInvitation? invitation = await inner.GetByTokenHashAsync(tokenHash, ct);
            Coordinate(ct);
            return invitation;
        }

        public Task<WorkspaceInvitation?> GetByHandoffHashAsync(
            string handoffHash,
            CancellationToken ct = default) =>
            inner.GetByHandoffHashAsync(handoffHash, ct);

        public Task<IReadOnlyList<WorkspaceInvitation>> ListForWorkspaceAsync(
            Guid workspaceId,
            int offset,
            int limit,
            CancellationToken ct = default) =>
            inner.ListForWorkspaceAsync(workspaceId, offset, limit, ct);

        public Task<int> CountForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) =>
            inner.CountForWorkspaceAsync(workspaceId, ct);

        public Task<IReadOnlyList<WorkspaceInvitation>> ListDueDeliveryAsync(
            DateTime now,
            int limit,
            CancellationToken ct = default) =>
            inner.ListDueDeliveryAsync(now, limit, ct);

        public Task<IReadOnlyList<WorkspaceInvitation>> ListDueExpiryAsync(
            DateTime now,
            int limit,
            CancellationToken ct = default) =>
            inner.ListDueExpiryAsync(now, limit, ct);

        public Task<IReadOnlyList<WorkspaceInvitation>> ListReadyForTerminalCleanupAsync(
            int limit,
            CancellationToken ct = default) =>
            inner.ListReadyForTerminalCleanupAsync(limit, ct);
    }

    private sealed class PassThroughRateLimiter : IWorkspaceInvitationRateLimiter
    {
        public Task<Result> AcquireCreateAsync(
            Guid inviterUserId,
            Guid workspaceId,
            string normalizedEmail,
            CancellationToken ct = default) => Task.FromResult(Result.Success());

        public Task<Result> AcquireResendAsync(
            Guid inviterUserId,
            Guid invitationId,
            CancellationToken ct = default) => Task.FromResult(Result.Success());

        public Task<Result> AcquireExchangeAsync(
            string requestPartition,
            string tokenHash,
            CancellationToken ct = default) => Task.FromResult(Result.Success());
    }

    private sealed class FixedTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;
    }

    private sealed class StaticEnvelopeProtector(InvitationDeliveryMessage message)
        : IInvitationDeliveryEnvelopeProtector
    {
        public string Protect(InvitationDeliveryMessage value) => "protected:delivery";

        public InvitationDeliveryMessage Unprotect(string protectedEnvelope)
        {
            protectedEnvelope.Should().Be("protected:delivery");
            return message;
        }
    }

    private sealed class AmbiguousEmailSender : IEmailSender
    {
        private bool failAfterFirstSend = true;

        public List<InvitationDeliveryMessage> Messages { get; } = [];

        public Task SendVerificationEmailAsync(
            string toEmail,
            string verificationToken,
            string language,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SendWorkspaceInvitationEmailAsync(
            InvitationDeliveryMessage message,
            CancellationToken ct = default)
        {
            Messages.Add(message);
            if (failAfterFirstSend)
            {
                failAfterFirstSend = false;
                throw new InvalidOperationException("Provider accepted the message before response loss.");
            }

            return Task.CompletedTask;
        }
    }
}
