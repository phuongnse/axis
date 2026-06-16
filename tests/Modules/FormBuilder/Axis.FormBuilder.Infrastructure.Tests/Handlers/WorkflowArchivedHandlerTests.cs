using axis.workflowbuilder.events;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Infrastructure.Handlers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Axis.FormBuilder.Infrastructure.Tests.Handlers;

[Collection("FormBuilderDb")]
public sealed class WorkflowArchivedHandlerTests(FormBuilderDatabaseFixture fixture)
{
    private static readonly Guid TeamAccountId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static WorkflowArchivedHandler CreateHandler(FormBuilderDbContext ctx)
    {
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(call => ctx.SaveChangesAsync(call.Arg<CancellationToken>()));
        ILogger<WorkflowArchivedHandler> logger = Substitute.For<ILogger<WorkflowArchivedHandler>>();
        return new WorkflowArchivedHandler(ctx, uow, logger);
    }

    private async Task SeedActiveRef(Guid workflowId, Guid formId, FormBuilderDbContext ctx)
    {
        ctx.FormWorkflowReferences.Add(FormWorkflowReference.Create(workflowId, formId, TeamAccountId));
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_WhenActiveReferencesExist_DeactivatesThem()
    {
        Guid workflowId = Guid.NewGuid();
        Guid formId1 = Guid.NewGuid();
        Guid formId2 = Guid.NewGuid();

        await using FormBuilderDbContext setupCtx = fixture.CreateContext();
        await SeedActiveRef(workflowId, formId1, setupCtx);

        await using FormBuilderDbContext setupCtx2 = fixture.CreateContext();
        await SeedActiveRef(workflowId, formId2, setupCtx2);

        await using FormBuilderDbContext handlerCtx = fixture.CreateContext();
        await CreateHandler(handlerCtx).Handle(
            new WorkflowArchivedEvent { workflowId = workflowId.ToString(), teamAccountId = TeamAccountId.ToString() },
            CancellationToken.None);

        await using FormBuilderDbContext readCtx = fixture.CreateContext();
        List<FormWorkflowReference> refs = await readCtx.FormWorkflowReferences
            .Where(r => r.WorkflowId == workflowId)
            .ToListAsync();

        refs.Should().HaveCount(2);
        refs.Should().AllSatisfy(r => r.IsActive.Should().BeFalse());
    }

    [Fact]
    public async Task Handle_WhenNoActiveReferences_DoesNotThrow()
    {
        await using FormBuilderDbContext ctx = fixture.CreateContext();
        Func<Task> act = () => CreateHandler(ctx).Handle(
            new WorkflowArchivedEvent
            {
                workflowId = Guid.NewGuid().ToString(),
                teamAccountId = TeamAccountId.ToString(),
            },
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
