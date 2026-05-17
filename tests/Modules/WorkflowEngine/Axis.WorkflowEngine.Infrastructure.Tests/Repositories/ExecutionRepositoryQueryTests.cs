using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Axis.WorkflowEngine.Infrastructure.Repositories;
using FluentAssertions;

namespace Axis.WorkflowEngine.Infrastructure.Tests.Repositories;

[Collection("WorkflowEngineDatabase")]
public sealed class ExecutionRepositoryQueryTests(WorkflowEngineDatabaseFixture fixture)
{
    private static readonly Guid OrgId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherOrgId = Guid.Parse("00000000-0000-0000-0000-000000000099");

    private static WorkflowExecution CreateExecution(Guid? workflowId = null, Guid? orgId = null)
        => WorkflowExecution.Create(
            workflowId ?? Guid.NewGuid(), orgId ?? OrgId,
            TriggerType.Manual, null,
            new Dictionary<string, object?> { ["key"] = "val" });

    // --- GetPagedAsync ---

    [Fact]
    public async Task GetPagedAsync_WhenExecutionsExist_ReturnsPagedSummaries()
    {
        Guid wfId = Guid.NewGuid();
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new(ctx);

        await repo.AddAsync(CreateExecution(wfId));
        await repo.AddAsync(CreateExecution(wfId));
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new(readCtx);
        (IReadOnlyList<ExecutionSummaryResponse> items, int total) =
            await readRepo.GetPagedAsync(OrgId, 1, 10);

        items.Should().HaveCountGreaterThanOrEqualTo(2);
        total.Should().BeGreaterThanOrEqualTo(2);
        items.Should().AllSatisfy(i => i.TriggerType.Should().Be("Manual"));
    }

    [Fact]
    public async Task GetPagedAsync_WithStatusFilter_ReturnsOnlyMatchingStatus()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new(ctx);

        WorkflowExecution pending = CreateExecution();
        WorkflowExecution failed = CreateExecution();
        failed.Start();
        failed.Fail("error");

        await repo.AddAsync(pending);
        await repo.AddAsync(failed);
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new(readCtx);
        (IReadOnlyList<ExecutionSummaryResponse> items, int total) =
            await readRepo.GetPagedAsync(OrgId, 1, 100, ExecutionStatus.Failed);

        items.Should().AllSatisfy(i => i.Status.Should().Be("Failed"));
        total.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetPagedByWorkflowAsync_WhenPage2Requested_ReturnsCorrectSlice()
    {
        Guid wfId = Guid.NewGuid();
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new(ctx);

        for (int i = 0; i < 5; i++)
            await repo.AddAsync(CreateExecution(wfId));
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new(readCtx);
        (IReadOnlyList<ExecutionSummaryResponse> page1, int total1) =
            await readRepo.GetPagedByWorkflowAsync(wfId, OrgId, 1, 3);
        (IReadOnlyList<ExecutionSummaryResponse> page2, int total2) =
            await readRepo.GetPagedByWorkflowAsync(wfId, OrgId, 2, 3);

        page1.Should().HaveCount(3);
        page2.Should().HaveCount(2);
        total1.Should().Be(5);
        total2.Should().Be(5);
        page1.Select(i => i.Id).Should().NotIntersectWith(page2.Select(i => i.Id));
    }

    // --- GetPagedByWorkflowAsync ---

    [Fact]
    public async Task GetPagedByWorkflowAsync_WhenFilteredByWorkflow_ReturnsOnlyThatWorkflow()
    {
        Guid wfA = Guid.NewGuid();
        Guid wfB = Guid.NewGuid();
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new(ctx);

        await repo.AddAsync(CreateExecution(wfA));
        await repo.AddAsync(CreateExecution(wfA));
        await repo.AddAsync(CreateExecution(wfB));
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new(readCtx);
        (IReadOnlyList<ExecutionSummaryResponse> items, int total) =
            await readRepo.GetPagedByWorkflowAsync(wfA, OrgId, 1, 10);

        items.Should().HaveCount(2);
        total.Should().Be(2);
        items.Should().AllSatisfy(i => i.WorkflowDefinitionId.Should().Be(wfA));
    }

    // --- GetWithStepsAsync ---

    [Fact]
    public async Task GetWithStepsAsync_WhenExecutionExistsWithNoSteps_ReturnsResponseWithEmptySteps()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new(ctx);

        WorkflowExecution exec = CreateExecution();
        await repo.AddAsync(exec);
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new(readCtx);
        ExecutionResponse? response = await readRepo.GetWithStepsAsync(exec.Id, OrgId);

        response.Should().NotBeNull();
        response!.Id.Should().Be(exec.Id);
        response.Status.Should().Be("Pending");
        response.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWithStepsAsync_WhenExecutionHasSteps_ReturnsStepsOrderedByDisplayOrder()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new(ctx);

        WorkflowExecution exec = CreateExecution();
        await repo.AddAsync(exec);

        ExecutionStep stepB = ExecutionStep.Create(exec.Id, OrgId, Guid.NewGuid(), "Step B", StepType.HttpRequest, 1);
        ExecutionStep stepA = ExecutionStep.Create(exec.Id, OrgId, Guid.NewGuid(), "Step A", StepType.Form, 0);
        ctx.ExecutionSteps.Add(stepA);
        ctx.ExecutionSteps.Add(stepB);
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new(readCtx);
        ExecutionResponse? response = await readRepo.GetWithStepsAsync(exec.Id, OrgId);

        response!.Steps.Should().HaveCount(2);
        response.Steps[0].Name.Should().Be("Step A");
        response.Steps[0].DisplayOrder.Should().Be(0);
        response.Steps[1].Name.Should().Be("Step B");
        response.Steps[1].DisplayOrder.Should().Be(1);
    }

    [Fact]
    public async Task GetWithStepsAsync_WhenOrgDoesNotMatch_ReturnsNull()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new(ctx);

        WorkflowExecution exec = CreateExecution();
        await repo.AddAsync(exec);
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new(readCtx);
        ExecutionResponse? response = await readRepo.GetWithStepsAsync(exec.Id, OtherOrgId);

        response.Should().BeNull();
    }

    // --- GetRetriesAsync ---

    [Fact]
    public async Task GetRetriesAsync_WhenRetriesExist_ReturnsChronologicalRetries()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new(ctx);

        WorkflowExecution original = CreateExecution();
        original.Start();
        original.Fail("error");
        await repo.AddAsync(original);
        await ctx.SaveChangesAsync();

        await Task.Delay(5);
        WorkflowExecution retry1 = original.CreateRetry(null);
        await repo.AddAsync(retry1);
        await ctx.SaveChangesAsync();

        await Task.Delay(5);
        WorkflowExecution retry2 = original.CreateRetryWithModifiedContext(
            null, new Dictionary<string, object?> { ["key"] = "fixed" });
        await repo.AddAsync(retry2);
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new(readCtx);
        IReadOnlyList<ExecutionSummaryResponse> retries = await readRepo.GetRetriesAsync(original.Id, OrgId);

        retries.Should().HaveCount(2);
        retries.Should().AllSatisfy(r => r.RetryOfExecutionId.Should().Be(original.Id));
        retries.Select(r => r.CreatedAt).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetRetriesAsync_WhenNoRetries_ReturnsEmptyList()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new(ctx);

        WorkflowExecution exec = CreateExecution();
        await repo.AddAsync(exec);
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new(readCtx);
        IReadOnlyList<ExecutionSummaryResponse> retries = await readRepo.GetRetriesAsync(exec.Id, OrgId);

        retries.Should().BeEmpty();
    }
}
