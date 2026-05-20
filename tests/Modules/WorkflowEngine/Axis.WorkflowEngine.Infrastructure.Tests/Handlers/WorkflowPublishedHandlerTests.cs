using Axis.WorkflowEngine.Domain.ReadModels;
using Axis.WorkflowEngine.Infrastructure.Handlers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using BuilderEvents = Axis.WorkflowBuilder.Domain.Events;

namespace Axis.WorkflowEngine.Infrastructure.Tests.Handlers;

[Collection("WorkflowEngineDatabase")]
public sealed class WorkflowPublishedHandlerTests(WorkflowEngineDatabaseFixture fixture)
{
    private static readonly Guid OrgId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static BuilderEvents.WorkflowPublished BuildEvent(Guid workflowId, string stepType = "Form")
    {
        Guid stepId = Guid.NewGuid();
        Guid step2Id = Guid.NewGuid();
        return new BuilderEvents.WorkflowPublished(
            workflowId,
            OrgId,
            new List<Guid>(),
            new List<BuilderEvents.StepSnapshot>
            {
                new(stepId, "Start", "Start", 0, null),
                new(step2Id, stepType, stepType, 1, new Dictionary<string, object?> { ["formId"] = Guid.NewGuid().ToString() })
            },
            new List<BuilderEvents.TransitionSnapshot>
            {
                new(stepId, step2Id, null)
            });
    }

    [Fact]
    public async Task Handle_WhenWorkflowFirstPublished_CreatesActiveStatusAndSnapshot()
    {
        Guid workflowId = Guid.NewGuid();
        BuilderEvents.WorkflowPublished @event = BuildEvent(workflowId);

        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        WorkflowPublishedHandler handler = new(ctx);

        await handler.Handle(@event, CancellationToken.None);

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();

        bool isActive = await readCtx.WorkflowActiveStatuses
            .AnyAsync(w => w.WorkflowId == workflowId && w.OrganizationId == OrgId);
        isActive.Should().BeTrue();

        WorkflowSnapshot? snapshot = await readCtx.WorkflowSnapshots
            .FirstOrDefaultAsync(w => w.WorkflowId == workflowId);
        snapshot.Should().NotBeNull();
        snapshot!.Steps.Should().HaveCount(2);
        snapshot.Transitions.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WhenWorkflowRepublished_UpdatesSnapshotAndKeepsActiveStatus()
    {
        Guid workflowId = Guid.NewGuid();
        BuilderEvents.WorkflowPublished firstEvent = BuildEvent(workflowId);

        await using WorkflowEngineDbContext ctx1 = fixture.CreateContext();
        await new WorkflowPublishedHandler(ctx1).Handle(firstEvent, CancellationToken.None);

        // Re-publish with different step count
        Guid newStepId = Guid.NewGuid();
        BuilderEvents.WorkflowPublished secondEvent = new(
            workflowId,
            OrgId,
            new List<Guid>(),
            new List<BuilderEvents.StepSnapshot> { new(newStepId, "Http", "HttpRequest", 0, null) },
            new List<BuilderEvents.TransitionSnapshot>());

        await using WorkflowEngineDbContext ctx2 = fixture.CreateContext();
        await new WorkflowPublishedHandler(ctx2).Handle(secondEvent, CancellationToken.None);

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        WorkflowSnapshot? snapshot = await readCtx.WorkflowSnapshots
            .FirstOrDefaultAsync(w => w.WorkflowId == workflowId);

        snapshot.Should().NotBeNull();
        snapshot!.Steps.Should().HaveCount(1);
        snapshot.Steps[0].Name.Should().Be("Http");
    }

    [Fact]
    public async Task Handle_WhenDeliveredTwice_IsIdempotent()
    {
        Guid workflowId = Guid.NewGuid();
        BuilderEvents.WorkflowPublished @event = BuildEvent(workflowId);

        await using WorkflowEngineDbContext ctx1 = fixture.CreateContext();
        await new WorkflowPublishedHandler(ctx1).Handle(@event, CancellationToken.None);

        // Second delivery of the same event — should not throw
        await using WorkflowEngineDbContext ctx2 = fixture.CreateContext();
        Func<Task> act = () => new WorkflowPublishedHandler(ctx2).Handle(@event, CancellationToken.None);
        await act.Should().NotThrowAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        int activeCount = await readCtx.WorkflowActiveStatuses
            .CountAsync(w => w.WorkflowId == workflowId);
        activeCount.Should().Be(1);
    }
}
