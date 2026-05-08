using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
    public async Task AddAsync_and_GetByIdAsync_round_trip()
    {
        await using var ctx = fixture.CreateContext();
        var repo = new ExecutionRepository(ctx);

        var execution = CreateExecution();
        await repo.AddAsync(execution);
        await ctx.SaveChangesAsync();

        await using var readCtx = fixture.CreateContext();
        var readRepo = new ExecutionRepository(readCtx);
        var loaded = await readRepo.GetByIdAsync(execution.Id, OrgId);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(execution.Id);
        loaded.Status.Should().Be(ExecutionStatus.Pending);
        loaded.TriggerType.Should().Be(TriggerType.Manual);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_wrong_org()
    {
        await using var ctx = fixture.CreateContext();
        var repo = new ExecutionRepository(ctx);

        var execution = CreateExecution(orgId: OrgId);
        await repo.AddAsync(execution);
        await ctx.SaveChangesAsync();

        await using var readCtx = fixture.CreateContext();
        var readRepo = new ExecutionRepository(readCtx);
        var result = await readRepo.GetByIdAsync(execution.Id, OtherOrgId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_returns_only_org_executions_ordered_newest_first()
    {
        await using var ctx = fixture.CreateContext();
        var repo = new ExecutionRepository(ctx);
        var wfId = Guid.NewGuid();

        var first = CreateExecution(workflowId: wfId);
        await Task.Delay(5); // ensure distinct CreatedAt
        var second = CreateExecution(workflowId: wfId);
        var other = CreateExecution(orgId: OtherOrgId);

        await repo.AddAsync(first);
        await repo.AddAsync(second);
        await repo.AddAsync(other);
        await ctx.SaveChangesAsync();

        await using var readCtx = fixture.CreateContext();
        var readRepo = new ExecutionRepository(readCtx);
        var results = await readRepo.GetAllAsync(OrgId);

        results.Should().HaveCountGreaterThanOrEqualTo(2);
        results.Should().OnlyContain(e => e.OrganizationId == OrgId);
        results.Select(e => e.CreatedAt).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetByWorkflowAsync_filters_by_workflow_and_org()
    {
        await using var ctx = fixture.CreateContext();
        var repo = new ExecutionRepository(ctx);
        var wfId = Guid.NewGuid();
        var otherWfId = Guid.NewGuid();

        var match = CreateExecution(workflowId: wfId);
        var wrongWf = CreateExecution(workflowId: otherWfId);
        var wrongOrg = CreateExecution(workflowId: wfId, orgId: OtherOrgId);

        await repo.AddAsync(match);
        await repo.AddAsync(wrongWf);
        await repo.AddAsync(wrongOrg);
        await ctx.SaveChangesAsync();

        await using var readCtx = fixture.CreateContext();
        var readRepo = new ExecutionRepository(readCtx);
        var results = await readRepo.GetByWorkflowAsync(wfId, OrgId);

        results.Should().ContainSingle();
        results[0].Id.Should().Be(match.Id);
    }

    [Fact]
    public async Task Context_dictionary_is_persisted_and_loaded()
    {
        await using var ctx = fixture.CreateContext();
        var repo = new ExecutionRepository(ctx);

        var execution = WorkflowExecution.Create(
            Guid.NewGuid(), OrgId, TriggerType.Webhook, null,
            new Dictionary<string, object?> { ["input_name"] = "Alice", ["input_count"] = 42 });

        await repo.AddAsync(execution);
        await ctx.SaveChangesAsync();

        await using var readCtx = fixture.CreateContext();
        var readRepo = new ExecutionRepository(readCtx);
        var loaded = await readRepo.GetByIdAsync(execution.Id, OrgId);

        loaded!.Context.Should().ContainKey("input_name");
        loaded.Context.Should().ContainKey("input_count");
    }

    [Fact]
    public async Task Status_transitions_are_persisted()
    {
        await using var ctx = fixture.CreateContext();
        var repo = new ExecutionRepository(ctx);

        var execution = CreateExecution();
        await repo.AddAsync(execution);
        await ctx.SaveChangesAsync();

        execution.Start();
        execution.Complete();
        await ctx.SaveChangesAsync();

        await using var readCtx = fixture.CreateContext();
        var readRepo = new ExecutionRepository(readCtx);
        var loaded = await readRepo.GetByIdAsync(execution.Id, OrgId);

        loaded!.Status.Should().Be(ExecutionStatus.Completed);
        loaded.StartedAt.Should().NotBeNull();
        loaded.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task WorkflowDefinitionReader_returns_true_for_active_workflow()
    {
        var wfId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"INSERT INTO \"test_workflow_engine\".workflow_definitions (id, organization_id, status) " +
            $"VALUES ('{wfId:D}', '{OrgId:D}', 'Active')";
        await cmd.ExecuteNonQueryAsync();

        await using var ctx = fixture.CreateContext();
        var reader = new WorkflowDefinitionReader(ctx);
        var result = await reader.IsActiveAsync(wfId, OrgId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task WorkflowDefinitionReader_returns_false_for_draft_workflow()
    {
        var wfId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"INSERT INTO \"test_workflow_engine\".workflow_definitions (id, organization_id, status) " +
            $"VALUES ('{wfId:D}', '{OrgId:D}', 'Draft')";
        await cmd.ExecuteNonQueryAsync();

        await using var ctx = fixture.CreateContext();
        var reader = new WorkflowDefinitionReader(ctx);
        var result = await reader.IsActiveAsync(wfId, OrgId);

        result.Should().BeFalse();
    }
}
