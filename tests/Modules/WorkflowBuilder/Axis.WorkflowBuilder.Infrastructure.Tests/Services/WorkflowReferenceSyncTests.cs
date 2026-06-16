using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using Axis.WorkflowBuilder.Domain.ReadModels;
using Axis.WorkflowBuilder.Infrastructure.Services;
using FluentAssertions;

namespace Axis.WorkflowBuilder.Infrastructure.Tests.Services;

[Collection("WorkflowBuilderDb")]
public sealed class WorkflowReferenceSyncTests(WorkflowBuilderDatabaseFixture fixture)
{
    private static readonly Guid OrgId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task SyncAsync_WhenReferenceIsBroken_ReturnsHasBrokenWithoutSave()
    {
        Guid formId = Guid.NewGuid();
        WorkflowDefinition workflow = WorkflowDefinition.Create($"Flow-{Guid.NewGuid():N}", null, OrgId, "user");
        workflow.AddStep("Intake", StepType.Form, new Dictionary<string, object?> { ["formId"] = formId });
        Guid stepId = workflow.Steps.Single(s => s.Type == StepType.Form).Id;

        await using (WorkflowBuilderDbContext seedCtx = fixture.CreateContext())
        {
            seedCtx.WorkflowDefinitions.Add(workflow);
            seedCtx.WorkflowFormReferences.Add(
                WorkflowFormReference.Create(workflow.Id, stepId, formId, OrgId, isBroken: true));
            await seedCtx.SaveChangesAsync();
        }

        await using WorkflowBuilderDbContext ctx = fixture.CreateContext();
        WorkflowDefinition loaded = (await ctx.WorkflowDefinitions.FindAsync(workflow.Id))!;
        WorkflowReferenceSync sync = new(ctx);

        WorkflowReferenceSyncResult result = await sync.SyncAsync(loaded, CancellationToken.None);

        result.HasBrokenReferences.Should().BeTrue();
    }
}
