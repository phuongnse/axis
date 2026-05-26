using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Domain.ReadModels;
using Axis.WorkflowEngine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.WorkflowEngine.Infrastructure.Tests.Repositories;

[Collection("WorkflowEngineDatabase")]
public sealed class ExecutionRepositoryTests(WorkflowEngineDatabaseFixture fixture)
{
    private static readonly Guid OrgId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherOrgId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static WorkflowExecution CreateExecution(
        Guid? workflowId = null, Guid? orgId = null,
        TriggerType trigger = TriggerType.Manual)
        => WorkflowExecution.Create(
            workflowId ?? Guid.NewGuid(),
            orgId ?? OrgId,
            trigger,
            null,
            new Dictionary<string, object?> { ["key"] = "value" });

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new ExecutionRepository(ctx);
        WorkflowExecution execution = CreateExecution();
        await repo.AddAsync(execution);
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new ExecutionRepository(readCtx);
        WorkflowExecution? loaded = await readRepo.GetByIdAsync(execution.Id, OrgId);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(execution.Id);
        loaded.Status.Should().Be(ExecutionStatus.Pending);
        loaded.TriggerType.Should().Be(TriggerType.Manual);
    }

    [Fact]
    public async Task GetByIdAsync_WhenOrgDoesNotMatch_ReturnsNull()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new ExecutionRepository(ctx);
        WorkflowExecution execution = CreateExecution(orgId: OrgId);
        await repo.AddAsync(execution);
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new ExecutionRepository(readCtx);
        WorkflowExecution? result = await readRepo.GetByIdAsync(execution.Id, OtherOrgId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_WhenMultipleExecutionsExist_ReturnsOnlyOrgExecutionsOrderedNewestFirst()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new ExecutionRepository(ctx);
        Guid wfId = Guid.NewGuid();
        WorkflowExecution first = CreateExecution(workflowId: wfId);
        await Task.Delay(5); // ensure distinct CreatedAt
        WorkflowExecution second = CreateExecution(workflowId: wfId);
        WorkflowExecution other = CreateExecution(orgId: OtherOrgId);

        await repo.AddAsync(first);
        await repo.AddAsync(second);
        await repo.AddAsync(other);
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new ExecutionRepository(readCtx);
        IReadOnlyList<WorkflowExecution> results = await readRepo.GetAllAsync(OrgId);

        results.Should().HaveCountGreaterThanOrEqualTo(2);
        results.Should().OnlyContain(e => e.OrganizationId == OrgId);
        results.Select(e => e.CreatedAt).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetByWorkflowAsync_WhenFilteredByWorkflowAndOrg_ReturnsMatchingExecutions()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new ExecutionRepository(ctx);
        Guid wfId = Guid.NewGuid();
        Guid otherWfId = Guid.NewGuid();
        WorkflowExecution match = CreateExecution(workflowId: wfId);
        WorkflowExecution wrongWf = CreateExecution(workflowId: otherWfId);
        WorkflowExecution wrongOrg = CreateExecution(workflowId: wfId, orgId: OtherOrgId);

        await repo.AddAsync(match);
        await repo.AddAsync(wrongWf);
        await repo.AddAsync(wrongOrg);
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new ExecutionRepository(readCtx);
        IReadOnlyList<WorkflowExecution> results = await readRepo.GetByWorkflowAsync(wfId, OrgId);

        results.Should().ContainSingle();
        results[0].Id.Should().Be(match.Id);
    }

    [Fact]
    public async Task AddAsync_WhenExecutionHasContextDictionary_PersistsAndLoadsContext()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new ExecutionRepository(ctx);
        WorkflowExecution execution = WorkflowExecution.Create(
                    Guid.NewGuid(), OrgId, TriggerType.Webhook, null,
                    new Dictionary<string, object?> { ["input_name"] = "Alice", ["input_count"] = 42 });

        await repo.AddAsync(execution);
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new ExecutionRepository(readCtx);
        WorkflowExecution? loaded = await readRepo.GetByIdAsync(execution.Id, OrgId);

        loaded!.Context.Should().ContainKey("input_name");
        loaded.Context.Should().ContainKey("input_count");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenStatusTransitionsOccur_PersistsStatusTransitions()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new ExecutionRepository(ctx);
        WorkflowExecution execution = CreateExecution();
        await repo.AddAsync(execution);
        await ctx.SaveChangesAsync();

        execution.Start();
        execution.Complete();
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new ExecutionRepository(readCtx);
        WorkflowExecution? loaded = await readRepo.GetByIdAsync(execution.Id, OrgId);

        loaded!.Status.Should().Be(ExecutionStatus.Completed);
        loaded.StartedAt.Should().NotBeNull();
        loaded.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task WorkflowDefinitionReader_WhenWorkflowIsActive_ReturnsTrue()
    {
        Guid wfId = Guid.NewGuid();

        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ctx.WorkflowActiveStatuses.Add(WorkflowActiveStatus.Activated(wfId, OrgId));
        await ctx.SaveChangesAsync();
        WorkflowDefinitionReader reader = new WorkflowDefinitionReader(ctx);
        bool result = await reader.IsActiveAsync(wfId, OrgId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task WorkflowDefinitionReader_WhenWorkflowIsNotActive_ReturnsFalse()
    {
        Guid wfId = Guid.NewGuid();

        // Workflow never published → no row in workflow_active_statuses → false
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        WorkflowDefinitionReader reader = new WorkflowDefinitionReader(ctx);
        bool result = await reader.IsActiveAsync(wfId, OrgId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdWithStepsAsync_WhenExecutionHasSteps_ReturnsExecutionWithAllSteps()
    {
        Guid wfId = Guid.NewGuid();
        Guid stepDefId = Guid.NewGuid();

        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new(ctx);

        WorkflowExecution execution = WorkflowExecution.Create(wfId, OrgId, TriggerType.Manual, null, new Dictionary<string, object?>());
        execution.AddStep(stepDefId, "Form", StepType.Form, 0);
        execution.AddStep(Guid.NewGuid(), "End", StepType.End, 1);

        await repo.AddAsync(execution);
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new(readCtx);
        WorkflowExecution? loaded = await readRepo.GetByIdWithStepsAsync(execution.Id, OrgId);

        loaded.Should().NotBeNull();
        loaded!.Steps.Should().HaveCount(2);
        loaded.Steps.Should().Contain(s => s.StepDefinitionId == stepDefId);
    }

    [Fact]
    public async Task GetByIdWithStepsAsync_WhenOrgDoesNotMatch_ReturnsNull()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        ExecutionRepository repo = new(ctx);

        WorkflowExecution execution = CreateExecution();
        await repo.AddAsync(execution);
        await ctx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        ExecutionRepository readRepo = new(readCtx);
        WorkflowExecution? result = await readRepo.GetByIdWithStepsAsync(execution.Id, OtherOrgId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task WorkflowDefinitionReader_GetSnapshotAsync_WhenSnapshotExists_ReturnsItWithStepsAndTransitions()
    {
        Guid wfId = Guid.NewGuid();
        Guid step1Id = Guid.NewGuid();
        Guid step2Id = Guid.NewGuid();

        IReadOnlyList<StepDefinitionSnapshot> steps = new List<StepDefinitionSnapshot>
        {
            new() { Id = step1Id, Name = "Start", StepType = StepType.Start, DisplayOrder = 0 },
            new() { Id = step2Id, Name = "Form", StepType = StepType.Form, DisplayOrder = 1, Config = new Dictionary<string, object?> { ["formId"] = Guid.NewGuid().ToString() } }
        };
        IReadOnlyList<TransitionSnapshot> transitions = new List<TransitionSnapshot>
        {
            new() { FromStepId = step1Id, ToStepId = step2Id }
        };

        await using WorkflowEngineDbContext setupCtx = fixture.CreateContext();
        setupCtx.WorkflowSnapshots.Add(WorkflowSnapshot.Create(wfId, OrgId, steps, transitions));
        await setupCtx.SaveChangesAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        WorkflowDefinitionReader reader = new(readCtx);
        WorkflowSnapshot? snapshot = await reader.GetSnapshotAsync(wfId, OrgId);

        snapshot.Should().NotBeNull();
        snapshot!.Steps.Should().HaveCount(2);
        snapshot.Transitions.Should().HaveCount(1);
        snapshot.Steps[1].Config.Should().ContainKey("formId");
    }

    [Fact]
    public async Task WorkflowDefinitionReader_GetSnapshotAsync_WhenNoSnapshot_ReturnsNull()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        WorkflowDefinitionReader reader = new(ctx);
        WorkflowSnapshot? result = await reader.GetSnapshotAsync(Guid.NewGuid(), OrgId);

        result.Should().BeNull();
    }
}
