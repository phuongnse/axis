using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;

namespace Axis.WorkflowBuilder.Infrastructure.Tests.Repositories;

[Collection("WorkflowBuilderDb")]
public class WorkflowRepositoryTests(WorkflowBuilderDatabaseFixture db) : IAsyncLifetime
{
    private WorkflowBuilderDbContext _ctx = null!;
    private WorkflowRepository _sut = null!;

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new WorkflowRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static WorkflowDefinition MakeWorkflow(string name, Guid? orgId = null)
        => WorkflowDefinition.Create(name, null, orgId ?? OrgId, UserId);

    [Fact]
    public async Task AddAsync_and_GetByIdAsync_round_trip()
    {
        var wf = MakeWorkflow("Order Approval");
        await _sut.AddAsync(wf);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(wf.Id, OrgId);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Order Approval");
        loaded.OrganizationId.Should().Be(OrgId);
        loaded.Status.Should().Be(WorkflowStatus.Draft);
    }

    [Fact]
    public async Task GetAllAsync_returns_only_org_workflows()
    {
        var orgId = Guid.NewGuid();
        var w1 = MakeWorkflow("WF-A", orgId);
        var w2 = MakeWorkflow("WF-B", orgId);
        var other = MakeWorkflow("WF-Other", Guid.NewGuid());

        await _sut.AddAsync(w1);
        await _sut.AddAsync(w2);
        await _sut.AddAsync(other);
        await _ctx.SaveChangesAsync();

        var result = await _sut.GetAllAsync(orgId);

        result.Should().HaveCount(2);
        result.Select(w => w.Name).Should().BeEquivalentTo(["WF-A", "WF-B"]);
    }

    [Fact]
    public async Task NameExistsAsync_is_case_insensitive()
    {
        var orgId = Guid.NewGuid();
        await _sut.AddAsync(MakeWorkflow("Employee Onboarding", orgId));
        await _ctx.SaveChangesAsync();

        (await _sut.NameExistsAsync("employee onboarding", orgId)).Should().BeTrue();
        (await _sut.NameExistsAsync("EMPLOYEE ONBOARDING", orgId)).Should().BeTrue();
    }

    [Fact]
    public async Task NameExistsAsync_excludes_self_on_update()
    {
        var orgId = Guid.NewGuid();
        var wf = MakeWorkflow("Invoice Review", orgId);
        await _sut.AddAsync(wf);
        await _ctx.SaveChangesAsync();

        var exists = await _sut.NameExistsAsync("Invoice Review", orgId, excludeId: wf.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task Steps_and_transitions_are_persisted_and_reloaded()
    {
        var wf = MakeWorkflow("Approval Flow");
        var step = wf.AddStep("Review", StepType.Form, new Dictionary<string, object?> { ["formId"] = "form-123" });
        wf.AddTransition(wf.Steps.First(s => s.Type == StepType.Start).Id, step.Id, null);
        wf.AddTransition(step.Id, wf.Steps.First(s => s.Type == StepType.End).Id, "approved");

        await _sut.AddAsync(wf);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(wf.Id, OrgId);

        loaded!.Steps.Should().HaveCount(3); // Start + Review + End
        loaded.Steps.Should().Contain(s => s.Name == "Review" && s.Type == StepType.Form);
        loaded.Transitions.Should().HaveCount(2);
        loaded.Transitions.Should().Contain(t => t.Label == "approved");
    }

    [Fact]
    public async Task Triggers_are_persisted_and_reloaded()
    {
        var wf = MakeWorkflow("Scheduled Report");
        wf.AddTrigger(TriggerType.Schedule, new Dictionary<string, object?> { ["cron"] = "0 9 * * 1" });
        wf.AddTrigger(TriggerType.Manual, null);

        await _sut.AddAsync(wf);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(wf.Id, OrgId);

        loaded!.Triggers.Should().HaveCount(2);
        loaded.Triggers.Should().Contain(t => t.Type == TriggerType.Schedule);
        loaded.Triggers.Should().Contain(t => t.Type == TriggerType.Manual);
    }

    [Fact]
    public async Task Status_transition_is_persisted()
    {
        WorkflowDefinition wf = MakeWorkflow("Published Flow");
        wf.AddStep("Notify", StepType.Notification, null);
        wf.AddTrigger(TriggerType.Manual, null);
        wf.Publish();

        await _sut.AddAsync(wf);
        await _ctx.SaveChangesAsync();

        WorkflowDefinition? loaded = await _sut.GetByIdAsync(wf.Id, OrgId);

        loaded!.Status.Should().Be(WorkflowStatus.Active);
    }

    [Fact]
    public async Task AddStep_AfterReload_IsPersisted()
    {
        Guid orgId = Guid.NewGuid();
        WorkflowDefinition wf = MakeWorkflow("Mutation Test", orgId);
        await _sut.AddAsync(wf);
        await _ctx.SaveChangesAsync();

        // Load then mutate in the same tracked context — without a ValueComparer EF Core
        // uses reference equality on the list and silently skips the UPDATE.
        WorkflowDefinition? loaded = await _sut.GetByIdAsync(wf.Id, orgId);
        loaded!.AddStep("Extra Step", StepType.Notification, null);
        await _ctx.SaveChangesAsync();

        // Verify with a fresh context to bypass the first-level cache
        await using WorkflowBuilderDbContext freshCtx = db.CreateContext();
        WorkflowRepository freshRepo = new(freshCtx);
        WorkflowDefinition? reloaded = await freshRepo.GetByIdAsync(wf.Id, orgId);

        reloaded!.Steps.Should().HaveCount(3, "Start + End + Extra Step must all be persisted");
        reloaded.Steps.Should().Contain(s => s.Name == "Extra Step");
    }

    [Fact]
    public async Task AddTrigger_AfterReload_IsPersisted()
    {
        Guid orgId = Guid.NewGuid();
        WorkflowDefinition wf = MakeWorkflow("Trigger Mutation Test", orgId);
        await _sut.AddAsync(wf);
        await _ctx.SaveChangesAsync();

        WorkflowDefinition? loaded = await _sut.GetByIdAsync(wf.Id, orgId);
        loaded!.AddTrigger(TriggerType.Manual, null);
        await _ctx.SaveChangesAsync();

        await using WorkflowBuilderDbContext freshCtx = db.CreateContext();
        WorkflowRepository freshRepo = new(freshCtx);
        WorkflowDefinition? reloaded = await freshRepo.GetByIdAsync(wf.Id, orgId);

        reloaded!.Triggers.Should().HaveCount(1);
        reloaded.Triggers.Should().Contain(t => t.Type == TriggerType.Manual);
    }
}
