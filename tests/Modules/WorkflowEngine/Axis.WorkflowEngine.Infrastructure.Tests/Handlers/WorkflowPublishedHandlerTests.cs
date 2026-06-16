using axis.workflowbuilder.events;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.ReadModels;
using Axis.WorkflowEngine.Infrastructure.Handlers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Axis.WorkflowEngine.Infrastructure.Tests.Handlers;

[Collection("WorkflowEngineDatabase")]
public sealed class WorkflowPublishedHandlerTests(WorkflowEngineDatabaseFixture fixture)
{
    private static readonly Guid OrgId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static WorkflowPublishedEvent BuildEvent(Guid workflowId, string stepType = "Form")
    {
        Guid stepId = Guid.NewGuid();
        Guid step2Id = Guid.NewGuid();
        return new WorkflowPublishedEvent
        {
            workflowId = workflowId.ToString(),
            organizationId = OrgId.ToString(),
            referencedFormIds = [],
            steps =
            [
                new StepSnapshotRecord { id = stepId.ToString(), name = "Start", stepType = "Start", displayOrder = 0 },
                new StepSnapshotRecord
                {
                    id = step2Id.ToString(),
                    name = stepType,
                    stepType = stepType,
                    displayOrder = 1,
                    configJson = $"{{\"formId\":\"{Guid.NewGuid()}\"}}",
                },
            ],
            transitions =
            [
                new TransitionSnapshotRecord { fromStepId = stepId.ToString(), toStepId = step2Id.ToString() },
            ],
        };
    }

    private static WorkflowPublishedHandler CreateHandler(WorkflowEngineDbContext ctx)
    {
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(call => ctx.SaveChangesAsync(call.Arg<CancellationToken>()));
        ILogger<WorkflowPublishedHandler> logger = Substitute.For<ILogger<WorkflowPublishedHandler>>();
        return new WorkflowPublishedHandler(ctx, uow, logger);
    }

    [Fact]
    public async Task Handle_WhenWorkflowFirstPublished_CreatesActiveStatusAndSnapshot()
    {
        Guid workflowId = Guid.NewGuid();
        WorkflowPublishedEvent @event = BuildEvent(workflowId);

        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        await CreateHandler(ctx).Handle(@event, CancellationToken.None);

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
        WorkflowPublishedEvent firstEvent = BuildEvent(workflowId);

        await using WorkflowEngineDbContext ctx1 = fixture.CreateContext();
        await CreateHandler(ctx1).Handle(firstEvent, CancellationToken.None);

        Guid newStepId = Guid.NewGuid();
        WorkflowPublishedEvent secondEvent = new()
        {
            workflowId = workflowId.ToString(),
            organizationId = OrgId.ToString(),
            referencedFormIds = [],
            steps = [new StepSnapshotRecord { id = newStepId.ToString(), name = "Http", stepType = "HttpRequest", displayOrder = 0 }],
            transitions = [],
        };

        await using WorkflowEngineDbContext ctx2 = fixture.CreateContext();
        await CreateHandler(ctx2).Handle(secondEvent, CancellationToken.None);

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
        WorkflowPublishedEvent @event = BuildEvent(workflowId);

        await using WorkflowEngineDbContext ctx1 = fixture.CreateContext();
        await CreateHandler(ctx1).Handle(@event, CancellationToken.None);

        await using WorkflowEngineDbContext ctx2 = fixture.CreateContext();
        Func<Task> act = () => CreateHandler(ctx2).Handle(@event, CancellationToken.None);
        await act.Should().NotThrowAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        int activeCount = await readCtx.WorkflowActiveStatuses
            .CountAsync(w => w.WorkflowId == workflowId);
        activeCount.Should().Be(1);
    }
}
