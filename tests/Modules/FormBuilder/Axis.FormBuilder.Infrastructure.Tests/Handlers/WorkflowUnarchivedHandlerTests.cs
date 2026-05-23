using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Infrastructure.Handlers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using axis.workflowbuilder.events;

namespace Axis.FormBuilder.Infrastructure.Tests.Handlers;

[Collection("FormBuilderDb")]
public sealed class WorkflowUnarchivedHandlerTests(FormBuilderDatabaseFixture fixture)
{
    private static readonly Guid OrgId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static WorkflowUnarchivedHandler CreateHandler(FormBuilderDbContext ctx)
    {
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(call => ctx.SaveChangesAsync(call.Arg<CancellationToken>()));
        ILogger<WorkflowUnarchivedHandler> logger = Substitute.For<ILogger<WorkflowUnarchivedHandler>>();
        return new WorkflowUnarchivedHandler(ctx, uow, logger);
    }

    private async Task SeedInactiveRef(Guid workflowId, Guid formId, FormBuilderDbContext ctx)
    {
        FormWorkflowReference r = FormWorkflowReference.Create(workflowId, formId, OrgId);
        r.Deactivate();
        ctx.FormWorkflowReferences.Add(r);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_WhenInactiveReferencesExist_ReactivatesThem()
    {
        Guid workflowId = Guid.NewGuid();
        Guid formId1 = Guid.NewGuid();
        Guid formId2 = Guid.NewGuid();

        await using FormBuilderDbContext setupCtx = fixture.CreateContext();
        await SeedInactiveRef(workflowId, formId1, setupCtx);

        await using FormBuilderDbContext setupCtx2 = fixture.CreateContext();
        await SeedInactiveRef(workflowId, formId2, setupCtx2);

        await using FormBuilderDbContext handlerCtx = fixture.CreateContext();
        await CreateHandler(handlerCtx).Handle(
            new WorkflowUnarchivedEvent
            {
                workflowId = workflowId.ToString(),
                organizationId = OrgId.ToString(),
                referencedFormIds = [],
            },
            CancellationToken.None);

        await using FormBuilderDbContext readCtx = fixture.CreateContext();
        List<FormWorkflowReference> refs = await readCtx.FormWorkflowReferences
            .Where(r => r.WorkflowId == workflowId)
            .ToListAsync();

        refs.Should().HaveCount(2);
        refs.Should().AllSatisfy(r => r.IsActive.Should().BeTrue());
    }

    [Fact]
    public async Task Handle_WhenNoInactiveReferences_DoesNotThrow()
    {
        await using FormBuilderDbContext ctx = fixture.CreateContext();
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
