using System.Text.Json;
using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Identity.Application.Commands.CompensateWorkspaceContextTransition;
using Axis.Identity.Application.Commands.CompleteWorkspaceContextTransition;
using Axis.Identity.Application.Commands.ExpireWorkspaceContextTransition;
using Axis.Identity.Application.Commands.FailWorkspaceContextTransition;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Tests.Commands;

[Collection("IdentityDb")]
public sealed class WorkspaceContextTransitionIntegrationTests(IdentityDatabaseFixture database)
{
    [Fact]
    public async Task AT003_WhenStagingFailsOrExpires_SourceRecoveryIsTheOnlyTerminalContext()
    {
        TransitionSeed stagingFailure = await SeedPendingAsync(DateTime.UtcNow);
        Result<WorkspaceContextTransitionDto> failed = await FailAsync(stagingFailure, "staging-failed");

        TransitionSeed expired = await SeedPendingAsync(DateTime.UtcNow.AddMinutes(-10));
        Result<WorkspaceContextTransitionDto> expiredResult = await ExpireAsync(expired);

        failed.IsSuccess.Should().BeTrue();
        failed.Value.Status.Should().Be("Failed");
        expiredResult.IsSuccess.Should().BeTrue();
        expiredResult.Value.Status.Should().Be("Compensated");

        await AssertSingleTerminalAuditAsync(stagingFailure.TransitionId, "failed");
        await AssertSingleTerminalAuditAsync(expired.TransitionId, "compensated");
    }

    [Fact]
    public async Task AT003_WhenConfirmAndRecoverRace_PostgreSqlCommitsOneTerminalContext()
    {
        TransitionSeed seed = await SeedPendingAsync(DateTime.UtcNow);

        Task<Result<WorkspaceContextTransitionDto>> confirm = Task.Run(() => CompleteAsync(seed, "confirm"));
        Task<Result<WorkspaceContextTransitionDto>> recover = Task.Run(() => CompensateAsync(seed, "recover"));
        Result<WorkspaceContextTransitionDto>[] results = await Task.WhenAll(confirm, recover);

        results.Should().OnlyContain(result => result.IsSuccess);
        results.Select(result => result.Value.Status).Distinct().Should().ContainSingle();

        await using IdentityDbContext observer = database.CreateContext();
        WorkspaceContextTransition transition = await observer.WorkspaceContextTransitions.SingleAsync(
            candidate => candidate.Id == seed.TransitionId,
            TestContext.Current.CancellationToken);
        transition.Status.Should().BeOneOf(
            WorkspaceContextTransitionStatus.Completed,
            WorkspaceContextTransitionStatus.Compensated);
        (await observer.Set<IdentityAuditOutboxRecord>().CountAsync(
            record => record.EventId == transition.TerminalAuditEventId,
            TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task AT008_WhenTransitionCompletes_PersistsCorrelatedRedactedRequestedAndTerminalAudits()
    {
        const string correlationId = "workspace-transition-correlation";
        TransitionSeed seed = await SeedEligibleUserAsync();
        Result<WorkspaceContextTransitionDto> begun = await BeginAsync(seed, correlationId);
        Result<WorkspaceContextTransitionDto> completed = await CompleteAsync(
            seed with { TransitionId = begun.Value.TransitionId },
            correlationId);

        begun.IsSuccess.Should().BeTrue();
        completed.IsSuccess.Should().BeTrue();
        completed.Value.Status.Should().Be("Completed");

        await using IdentityDbContext observer = database.CreateContext();
        WorkspaceContextTransition transition = await observer.WorkspaceContextTransitions.SingleAsync(
            candidate => candidate.Id == begun.Value.TransitionId,
            TestContext.Current.CancellationToken);
        List<IdentityAuditOutboxRecord> audit = await observer.Set<IdentityAuditOutboxRecord>()
            .Where(record => record.TargetId == transition.Id)
            .OrderBy(record => record.Outcome)
            .ToListAsync(TestContext.Current.CancellationToken);

        audit.Should().HaveCount(2);
        audit.Select(record => record.EventId).Should().BeEquivalentTo(
            new[] { transition.Id, transition.TerminalAuditEventId });
        audit.Should().OnlyContain(record => record.Action == "workspace.context.transition"
            && record.TargetType == "WorkspaceContextTransition"
            && record.WorkspaceId == seed.TargetWorkspaceId
            && record.ActorId == seed.UserId
            && record.SubjectId == seed.UserId
            && record.CorrelationId == correlationId
            && record.Status == IdentityAuditOutboxStatus.Pending
            && !record.MetadataJson.Contains(seed.SourceDigest, StringComparison.Ordinal)
            && !record.MetadataJson.Contains(seed.TargetDigest, StringComparison.Ordinal));
        audit.Select(record => record.Outcome).Should().BeEquivalentTo(new[] { "requested", "completed" });
        audit.Should().OnlyContain(record => IsRedactedTransitionMetadata(record.MetadataJson, transition.Id));
    }

    private async Task AssertSingleTerminalAuditAsync(Guid transitionId, string outcome)
    {
        await using IdentityDbContext observer = database.CreateContext();
        WorkspaceContextTransition transition = await observer.WorkspaceContextTransitions.SingleAsync(
            candidate => candidate.Id == transitionId,
            TestContext.Current.CancellationToken);
        transition.Status.Should().NotBe(WorkspaceContextTransitionStatus.Pending);
        IdentityAuditOutboxRecord audit = await observer.Set<IdentityAuditOutboxRecord>().SingleAsync(
            record => record.EventId == transition.TerminalAuditEventId,
            TestContext.Current.CancellationToken);
        audit.Outcome.Should().Be(outcome);
    }

    private static bool IsRedactedTransitionMetadata(string metadataJson, Guid transitionId)
    {
        using JsonDocument metadata = JsonDocument.Parse(metadataJson);
        return metadata.RootElement.ValueKind == JsonValueKind.Object
            && metadata.RootElement.EnumerateObject().Count() == 1
            && metadata.RootElement.TryGetProperty("transitionId", out JsonElement id)
            && id.GetString() == transitionId.ToString();
    }

    private async Task<TransitionSeed> SeedPendingAsync(DateTime createdAt)
    {
        TransitionSeed seed = await SeedEligibleUserAsync();
        WorkspaceContextTransition transition = WorkspaceContextTransition.Begin(
            seed.UserId,
            seed.SourceWorkspaceId,
            seed.TargetWorkspaceId,
            seed.SourceDigest,
            seed.TargetDigest,
            createdAt,
            createdAt.AddMinutes(5),
            createdAt.AddHours(1));
        await using IdentityDbContext context = database.CreateContext();
        context.WorkspaceContextTransitions.Add(transition);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return seed with { TransitionId = transition.Id };
    }

    private async Task<TransitionSeed> SeedEligibleUserAsync()
    {
        User user = User.Create("Transition User", Email.Create($"transition-{Guid.NewGuid():N}@example.com").Value);
        Workspace source = Workspace.CreatePersonal("Source", WorkspaceSlug.Create($"source-{Guid.NewGuid():N}").Value);
        Workspace target = Workspace.CreatePersonal("Target", WorkspaceSlug.Create($"target-{Guid.NewGuid():N}").Value);
        source.ActivateAfterOwnerVerification();
        target.ActivateAfterOwnerVerification();
        WorkspaceMembership sourceMembership = WorkspaceMembership.CreatePersonalOwner(source.Id, user.Id);
        WorkspaceMembership targetMembership = WorkspaceMembership.CreatePersonalOwner(target.Id, user.Id);
        await using IdentityDbContext context = database.CreateContext();
        context.AddRange(user, source, target, sourceMembership, targetMembership);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        Guid sourceDigestSeed = Guid.NewGuid();
        Guid targetDigestSeed = Guid.NewGuid();
        return new TransitionSeed(
            user.Id,
            source.Id,
            target.Id,
            sourceDigestSeed.ToString("N") + sourceDigestSeed.ToString("N"),
            targetDigestSeed.ToString("N") + targetDigestSeed.ToString("N"),
            Guid.Empty);
    }

    private async Task<Result<WorkspaceContextTransitionDto>> BeginAsync(TransitionSeed seed, string correlationId)
    {
        await using IdentityDbContext context = database.CreateContext();
        return await new BeginWorkspaceContextTransitionHandler(
            new WorkspaceMembershipRepository(context),
            new WorkspaceContextTransitionRepository(context),
            new IdentityAuditOutbox(context),
            new IdentityUnitOfWork(context),
            TimeProvider.System,
            new WorkspaceContextTransitionPolicy(TimeSpan.FromMinutes(5), TimeSpan.FromHours(1)))
            .Handle(new BeginWorkspaceContextTransitionCommand(
                seed.UserId, seed.SourceWorkspaceId, seed.TargetWorkspaceId,
                seed.SourceDigest, seed.TargetDigest, correlationId), TestContext.Current.CancellationToken);
    }

    private async Task<Result<WorkspaceContextTransitionDto>> CompleteAsync(TransitionSeed seed, string correlationId)
    {
        await using IdentityDbContext context = database.CreateContext();
        return await new CompleteWorkspaceContextTransitionHandler(
            new WorkspaceContextTransitionRepository(context),
            new WorkspaceMembershipRepository(context),
            new IdentityAuditOutbox(context),
            new IdentityUnitOfWork(context),
            TimeProvider.System)
            .Handle(new CompleteWorkspaceContextTransitionCommand(
                seed.TransitionId, seed.UserId, seed.TargetDigest, correlationId), TestContext.Current.CancellationToken);
    }

    private async Task<Result<WorkspaceContextTransitionDto>> CompensateAsync(TransitionSeed seed, string correlationId)
    {
        await using IdentityDbContext context = database.CreateContext();
        return await new CompensateWorkspaceContextTransitionHandler(
            new WorkspaceContextTransitionRepository(context),
            new IdentityAuditOutbox(context),
            new IdentityUnitOfWork(context),
            TimeProvider.System)
            .Handle(new CompensateWorkspaceContextTransitionCommand(
                seed.TransitionId, seed.UserId, seed.SourceDigest, correlationId), TestContext.Current.CancellationToken);
    }

    private async Task<Result<WorkspaceContextTransitionDto>> FailAsync(TransitionSeed seed, string correlationId)
    {
        await using IdentityDbContext context = database.CreateContext();
        return await new FailWorkspaceContextTransitionHandler(
            new WorkspaceContextTransitionRepository(context),
            new IdentityAuditOutbox(context),
            new IdentityUnitOfWork(context),
            TimeProvider.System)
            .Handle(new FailWorkspaceContextTransitionCommand(
                seed.TransitionId, seed.UserId, seed.SourceDigest, correlationId), TestContext.Current.CancellationToken);
    }

    private async Task<Result<WorkspaceContextTransitionDto>> ExpireAsync(TransitionSeed seed)
    {
        await using IdentityDbContext context = database.CreateContext();
        return await new ExpireWorkspaceContextTransitionHandler(
            new WorkspaceContextTransitionRepository(context),
            new IdentityAuditOutbox(context),
            new IdentityUnitOfWork(context),
            TimeProvider.System)
            .Handle(new ExpireWorkspaceContextTransitionCommand(
                seed.TransitionId, seed.UserId, seed.SourceDigest), TestContext.Current.CancellationToken);
    }

    private sealed record TransitionSeed(
        Guid UserId,
        Guid SourceWorkspaceId,
        Guid TargetWorkspaceId,
        string SourceDigest,
        string TargetDigest,
        Guid TransitionId);
}
