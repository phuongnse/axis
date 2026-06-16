using axis.datamodeling.events;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using Axis.WorkflowBuilder.Domain.ReadModels;
using Axis.WorkflowBuilder.Domain.ValueObjects;
using Axis.WorkflowBuilder.Infrastructure.Handlers;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Axis.WorkflowBuilder.Infrastructure.Tests.Handlers;

[Collection("WorkflowBuilderDb")]
public sealed class ModelDeletedHandlerTests(WorkflowBuilderDatabaseFixture fixture)
{
    private static readonly Guid TeamAccountId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static ModelDeletedHandler CreateHandler(WorkflowBuilderDbContext ctx)
    {
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(call => ctx.SaveChangesAsync(call.Arg<CancellationToken>()));
        ILogger<ModelDeletedHandler> logger = Substitute.For<ILogger<ModelDeletedHandler>>();
        return new ModelDeletedHandler(ctx, uow, logger);
    }

    private static ModelDeletedEvent BuildEvent(Guid modelId) =>
        new()
        {
            modelId = modelId.ToString(),
            teamAccountId = TeamAccountId.ToString(),
        };

    [Fact]
    public async Task Handle_WhenEventTriggerReferencesDeletedModel_MarksReferenceBroken()
    {
        Guid modelId = Guid.NewGuid();
        WorkflowDefinition workflow = WorkflowDefinition.Create($"Flow-{Guid.NewGuid():N}", null, TeamAccountId, "user");
        workflow.AddTrigger(
            TriggerType.Event,
            new Dictionary<string, object?> { ["eventType"] = "record.created", ["modelId"] = modelId });

        await using (WorkflowBuilderDbContext writeCtx = fixture.CreateContext())
        {
            writeCtx.WorkflowDefinitions.Add(workflow);
            writeCtx.WorkflowModelReferences.Add(
                WorkflowModelReference.Create(workflow.Id, modelId, TeamAccountId, isBroken: false));
            await writeCtx.SaveChangesAsync();
        }

        await using WorkflowBuilderDbContext handlerCtx = fixture.CreateContext();
        await CreateHandler(handlerCtx).Handle(BuildEvent(modelId), CancellationToken.None);

        await using WorkflowBuilderDbContext readCtx = fixture.CreateContext();
        WorkflowModelReference reference = await readCtx.WorkflowModelReferences
            .SingleAsync(r => r.WorkflowId == workflow.Id);

        reference.IsBroken.Should().BeTrue();
    }
}
