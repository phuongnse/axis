using Axis.FormBuilder.Domain.Events;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Domain.ReadModels;
using Axis.WorkflowEngine.Infrastructure.Handlers;
using Axis.WorkflowEngine.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Axis.WorkflowEngine.Infrastructure.Tests.Handlers;

[Collection("WorkflowEngineDatabase")]
public sealed class FormTaskSubmittedHandlerTests(WorkflowEngineDatabaseFixture fixture)
{
    private static readonly Guid OrgId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static WorkflowExecution CreateExecution()
        => WorkflowExecution.Create(Guid.NewGuid(), OrgId, TriggerType.Manual, null, new Dictionary<string, object?>());

    private static StepDefinitionSnapshot FormStepDef(Guid formId) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Form",
        StepType = StepType.Form,
        DisplayOrder = 0,
        Config = new Dictionary<string, object?> { ["formId"] = formId.ToString() }
    };

    private async Task<(WorkflowExecution Execution, ExecutionStep Step)> SeedWaitingFormStep(
        Guid formId, WorkflowEngineDbContext ctx)
    {
        WorkflowExecution exec = CreateExecution();
        exec.InitialiseSteps(new List<StepDefinitionSnapshot> { FormStepDef(formId) });
        ExecutionStep step = exec.Steps[0];

        exec.Start();
        exec.StartStep(step.Id, exec.Context);
        exec.ReachFormStep(step.Id, formId, null, null); // step → Waiting

        ctx.WorkflowExecutions.Add(exec);
        await ctx.SaveChangesAsync();
        return (exec, step);
    }

    private static FormTaskSubmittedHandler CreateHandler(
        WorkflowEngineDbContext ctx,
        IStepDispatcher dispatcher,
        ILogger<FormTaskSubmittedHandler> logger)
    {
        ExecutionRepository execRepo = new(ctx);
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(call => ctx.SaveChangesAsync(call.Arg<CancellationToken>()));
        return new FormTaskSubmittedHandler(execRepo, uow, dispatcher, logger);
    }

    [Fact]
    public async Task Handle_WhenFormSubmitted_CompletesStepMergesContextAndDispatchesNext()
    {
        Guid formId = Guid.NewGuid();
        IStepDispatcher dispatcher = Substitute.For<IStepDispatcher>();
        ILogger<FormTaskSubmittedHandler> logger = Substitute.For<ILogger<FormTaskSubmittedHandler>>();

        await using WorkflowEngineDbContext setupCtx = fixture.CreateContext();
        (WorkflowExecution execution, ExecutionStep step) = await SeedWaitingFormStep(formId, setupCtx);

        Dictionary<string, object?> submittedData = new() { ["answer"] = "yes" };
        FormTaskSubmitted @event = new(
            Guid.NewGuid(), formId, OrgId,
            execution.Id, step.Id, submittedData);

        await using WorkflowEngineDbContext handlerCtx = fixture.CreateContext();
        await CreateHandler(handlerCtx, dispatcher, logger).Handle(@event, CancellationToken.None);

        await using WorkflowEngineDbContext readCtx = fixture.CreateContext();
        WorkflowExecution? loaded = await readCtx.WorkflowExecutions
            .Include(e => e.Steps)
            .FirstOrDefaultAsync(e => e.Id == execution.Id);

        loaded.Should().NotBeNull();
        loaded!.Steps[0].Status.Should().Be(StepExecutionStatus.Completed);
        loaded.Context.Should().ContainKey("answer");

        await dispatcher.Received(1).PublishAsync(
            Arg.Is<ExecuteNextStepMessage>(m => m.ExecutionId == execution.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenStepAlreadyCompleted_IsIdempotentAndDoesNotDispatch()
    {
        Guid formId = Guid.NewGuid();
        IStepDispatcher dispatcher = Substitute.For<IStepDispatcher>();
        ILogger<FormTaskSubmittedHandler> logger = Substitute.For<ILogger<FormTaskSubmittedHandler>>();

        await using WorkflowEngineDbContext setupCtx = fixture.CreateContext();
        (WorkflowExecution execution, ExecutionStep step) = await SeedWaitingFormStep(formId, setupCtx);

        WorkflowExecution? execToAdvance = await setupCtx.WorkflowExecutions
            .Include(e => e.Steps)
            .FirstOrDefaultAsync(e => e.Id == execution.Id);
        execToAdvance!.CompleteStep(step.Id, new Dictionary<string, object?>());
        await setupCtx.SaveChangesAsync();

        FormTaskSubmitted @event = new(
            Guid.NewGuid(), formId, OrgId,
            execution.Id, step.Id,
            new Dictionary<string, object?> { ["answer"] = "yes" });

        await using WorkflowEngineDbContext handlerCtx = fixture.CreateContext();
        await CreateHandler(handlerCtx, dispatcher, logger).Handle(@event, CancellationToken.None);

        await dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<ExecuteNextStepMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenExecutionNotFound_ReturnsWithoutAction()
    {
        IStepDispatcher dispatcher = Substitute.For<IStepDispatcher>();
        ILogger<FormTaskSubmittedHandler> logger = Substitute.For<ILogger<FormTaskSubmittedHandler>>();

        FormTaskSubmitted @event = new(
            Guid.NewGuid(), Guid.NewGuid(), OrgId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Dictionary<string, object?>());

        await using WorkflowEngineDbContext ctx = fixture.CreateContext();
        await CreateHandler(ctx, dispatcher, logger).Handle(@event, CancellationToken.None);

        await dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}
