using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Infrastructure.Handlers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using axis.workflowbuilder.events;

namespace Axis.WorkflowEngine.Infrastructure.Tests.Handlers;

[Collection("WorkflowEngineDatabase")]
public sealed class WorkflowUnarchivedHandlerTests(WorkflowEngineDatabaseFixture fixture)
{
    private static readonly Guid OrgId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static WorkflowUnarchivedHandler CreateHandler(WorkflowEngineDbContext ctx)
    {
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(call => ctx.SaveChangesAsync(call.Arg<CancellationToken>()));
        ILogger<WorkflowUnarchivedHandler> logger = Substitute.For<ILogger<WorkflowUnarchivedHandler>>();
        return new WorkflowUnarchivedHandler(ctx, uow, logger);
    }

    [Fact]
    public async Task Handle_WhenWorkflowIsInactive_ReactivatesIt()
    {
        Guid workflowId = Guid.NewGuid();

        await using WorkflowEngineDbContext setupCtx = fixture.CreateContext();
        WorkflowActiveStatus status = WorkflowActiveStatus.Activated(workflowId, OrgId);
        status.Deactivate();
        setupCtx.WorkflowActiveStatuses.Add(status);
        await setupCtx.SaveChangesAsync();

        await using WorkflowEngineDbContext handlerCtx = fixture.CreateContext();
        await CreateHandler(handlerCtx).Handle(
            new WorkflowUnarchivedEvent
            {
                workflowId = workflowId.ToString(),
                organizationId = OrgId.ToString(),
                referencedFormIds = [],
            },
            CancellationToken.None);

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        WorkflowActiveStatus? loaded = await readCtx.WorkflowActiveStatuses
            .FirstOrDefaultAsync(w => w.WorkflowId == workflowId);

        loaded.Should().NotBeNull();
        loaded!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_DoesNotThrow()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        Func<Task> act = () => CreateHandler(ctx).Handle(
            new WorkflowUnarchivedEvent
            {
                workflowId = Guid.NewGuid().ToString(),
                organizationId = OrgId.ToString(),
                referencedFormIds = [],
            },
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
