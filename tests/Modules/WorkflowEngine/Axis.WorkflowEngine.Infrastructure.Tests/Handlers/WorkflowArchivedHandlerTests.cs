using axis.workflowbuilder.events;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Infrastructure.Handlers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Axis.WorkflowEngine.Infrastructure.Tests.Handlers;

[Collection("WorkflowEngineDatabase")]
public sealed class WorkflowArchivedHandlerTests(WorkflowEngineDatabaseFixture fixture)
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static WorkflowArchivedHandler CreateHandler(WorkflowEngineDbContext ctx)
    {
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(call => ctx.SaveChangesAsync(call.Arg<CancellationToken>()));
        ILogger<WorkflowArchivedHandler> logger = Substitute.For<ILogger<WorkflowArchivedHandler>>();
        return new WorkflowArchivedHandler(ctx, uow, logger);
    }

    [Fact]
    public async Task Handle_WhenWorkflowIsActive_DeactivatesIt()
    {
        Guid workflowId = Guid.NewGuid();

        await using WorkflowEngineDbContext setupCtx = fixture.CreateContext();
        setupCtx.WorkflowActiveStatuses.Add(WorkflowActiveStatus.Activated(workflowId, TenantId));
        await setupCtx.SaveChangesAsync();

        await using WorkflowEngineDbContext handlerCtx = fixture.CreateContext();
        await CreateHandler(handlerCtx).Handle(
            new WorkflowArchivedEvent { workflowId = workflowId.ToString(), tenantId = TenantId.ToString() },
            CancellationToken.None);

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        WorkflowActiveStatus? status = await readCtx.WorkflowActiveStatuses
            .FirstOrDefaultAsync(w => w.WorkflowId == workflowId);

        status.Should().NotBeNull();
        status!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_DoesNotThrow()
    {
        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        Func<Task> act = () => CreateHandler(ctx).Handle(
            new WorkflowArchivedEvent
            {
                workflowId = Guid.NewGuid().ToString(),
                tenantId = TenantId.ToString(),
            },
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_WhenDeliveredTwice_IsIdempotent()
    {
        Guid workflowId = Guid.NewGuid();

        await using WorkflowEngineDbContext setupCtx = fixture.CreateContext();
        setupCtx.WorkflowActiveStatuses.Add(WorkflowActiveStatus.Activated(workflowId, TenantId));
        await setupCtx.SaveChangesAsync();

        WorkflowArchivedEvent @event = new()
        {
            workflowId = workflowId.ToString(),
            tenantId = TenantId.ToString(),
        };

        await using WorkflowEngineDbContext ctx1 = fixture.CreateContext();
        await CreateHandler(ctx1).Handle(@event, CancellationToken.None);

        // Second delivery — status already inactive, handler skips without error
        await using WorkflowEngineDbContext ctx2 = fixture.CreateContext();
        Func<Task> act = () => CreateHandler(ctx2).Handle(@event, CancellationToken.None);
        await act.Should().NotThrowAsync();

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        WorkflowActiveStatus? status = await readCtx.WorkflowActiveStatuses
            .FirstOrDefaultAsync(w => w.WorkflowId == workflowId);
        status!.IsActive.Should().BeFalse();
    }
}
