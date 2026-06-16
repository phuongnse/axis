using axis.formbuilder.events;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using Axis.WorkflowBuilder.Domain.ReadModels;
using Axis.WorkflowBuilder.Infrastructure.Handlers;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Axis.WorkflowBuilder.Infrastructure.Tests.Handlers;

[Collection("WorkflowBuilderDb")]
public sealed class FormDeletedHandlerTests(WorkflowBuilderDatabaseFixture fixture)
{
    private static readonly Guid WorkspaceId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static FormDeletedHandler CreateHandler(WorkflowBuilderDbContext ctx)
    {
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(call => ctx.SaveChangesAsync(call.Arg<CancellationToken>()));
        ILogger<FormDeletedHandler> logger = Substitute.For<ILogger<FormDeletedHandler>>();
        return new FormDeletedHandler(ctx, uow, logger);
    }

    private static FormDeletedEvent BuildEvent(Guid formId) =>
        new()
        {
            formId = formId.ToString(),
            workspaceId = WorkspaceId.ToString(),
        };

    [Fact]
    public async Task Handle_WhenFormStepReferencesDeletedForm_MarksReferenceBroken()
    {
        Guid formId = Guid.NewGuid();
        WorkflowDefinition workflow = WorkflowDefinition.Create($"Flow-{Guid.NewGuid():N}", null, WorkspaceId, "user");
        workflow.AddStep("Intake", StepType.Form, new Dictionary<string, object?> { ["formId"] = formId });
        Guid stepId = workflow.Steps.Single(s => s.Type == StepType.Form).Id;

        await using (WorkflowBuilderDbContext writeCtx = fixture.CreateContext())
        {
            writeCtx.WorkflowDefinitions.Add(workflow);
            writeCtx.WorkflowFormReferences.Add(
                WorkflowFormReference.Create(workflow.Id, stepId, formId, WorkspaceId, isBroken: false));
            await writeCtx.SaveChangesAsync();
        }

        await using WorkflowBuilderDbContext handlerCtx = fixture.CreateContext();
        await CreateHandler(handlerCtx).Handle(BuildEvent(formId), CancellationToken.None);

        await using WorkflowBuilderDbContext readCtx = fixture.CreateContext();
        WorkflowFormReference reference = await readCtx.WorkflowFormReferences
            .SingleAsync(r => r.WorkflowId == workflow.Id && r.StepId == stepId);

        reference.IsBroken.Should().BeTrue();
    }
}
