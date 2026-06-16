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
public sealed class WorkflowPublishedHandlerTests(FormBuilderDatabaseFixture fixture)
{
    private static readonly Guid TeamAccountId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static WorkflowPublishedHandler CreateHandler(FormBuilderDbContext ctx)
    {
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(call => ctx.SaveChangesAsync(call.Arg<CancellationToken>()));
        ILogger<WorkflowPublishedHandler> logger = Substitute.For<ILogger<WorkflowPublishedHandler>>();
        return new WorkflowPublishedHandler(ctx, uow, logger);
    }

    private static WorkflowPublishedEvent BuildEvent(Guid workflowId, IReadOnlyList<Guid> formIds) =>
        new()
        {
            workflowId = workflowId.ToString(),
            teamAccountId = TeamAccountId.ToString(),
            referencedFormIds = formIds.Select(id => id.ToString()).ToList(),
            steps = [],
            transitions = [],
        };

    [Fact]
    public async Task Handle_WhenWorkflowFirstPublished_CreatesFormReferences()
    {
        Guid workflowId = Guid.NewGuid();
        Guid formId1 = Guid.NewGuid();
        Guid formId2 = Guid.NewGuid();

        await using FormBuilderDbContext ctx = fixture.CreateContext();
        await CreateHandler(ctx).Handle(
            BuildEvent(workflowId, new List<Guid> { formId1, formId2 }),
            CancellationToken.None);

        await using FormBuilderDbContext readCtx = fixture.CreateContext();
        List<FormWorkflowReference> refs = await readCtx.FormWorkflowReferences
            .Where(r => r.WorkflowId == workflowId)
            .ToListAsync();

        refs.Should().HaveCount(2);
        refs.Should().AllSatisfy(r => r.IsActive.Should().BeTrue());
    }

    [Fact]
    public async Task Handle_WhenWorkflowRepublished_SyncsReferences()
    {
        Guid workflowId = Guid.NewGuid();
        Guid oldFormId = Guid.NewGuid();
        Guid newFormId = Guid.NewGuid();

        await using FormBuilderDbContext ctx1 = fixture.CreateContext();
        await CreateHandler(ctx1).Handle(
            BuildEvent(workflowId, new List<Guid> { oldFormId }),
            CancellationToken.None);

        await using FormBuilderDbContext ctx2 = fixture.CreateContext();
        await CreateHandler(ctx2).Handle(
            BuildEvent(workflowId, new List<Guid> { newFormId }),
            CancellationToken.None);

        await using FormBuilderDbContext readCtx = fixture.CreateContext();
        List<FormWorkflowReference> refs = await readCtx.FormWorkflowReferences
            .Where(r => r.WorkflowId == workflowId)
            .ToListAsync();

        refs.Should().HaveCount(1);
        refs[0].FormId.Should().Be(newFormId);
        refs[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenReferencedFormIdsContainDuplicates_PersistsOneRowPerForm()
    {
        Guid workflowId = Guid.NewGuid();
        Guid formId = Guid.NewGuid();

        await using FormBuilderDbContext ctx = fixture.CreateContext();
        await CreateHandler(ctx).Handle(
            BuildEvent(workflowId, new List<Guid> { formId, formId }),
            CancellationToken.None);

        await using FormBuilderDbContext readCtx = fixture.CreateContext();
        int count = await readCtx.FormWorkflowReferences
            .CountAsync(r => r.WorkflowId == workflowId && r.FormId == formId);
        count.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenDeliveredTwice_IsIdempotent()
    {
        Guid workflowId = Guid.NewGuid();
        Guid formId = Guid.NewGuid();
        WorkflowPublishedEvent @event = BuildEvent(workflowId, new List<Guid> { formId });

        await using FormBuilderDbContext ctx1 = fixture.CreateContext();
        await CreateHandler(ctx1).Handle(@event, CancellationToken.None);

        // Second delivery of the same event — should not throw
        await using FormBuilderDbContext ctx2 = fixture.CreateContext();
        Func<Task> act = () => CreateHandler(ctx2).Handle(@event, CancellationToken.None);
        await act.Should().NotThrowAsync();

        await using FormBuilderDbContext readCtx = fixture.CreateContext();
        int count = await readCtx.FormWorkflowReferences
            .CountAsync(r => r.WorkflowId == workflowId && r.FormId == formId);
        count.Should().Be(1);
    }
}
