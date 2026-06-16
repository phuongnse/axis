using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.ImportWorkflow;
using Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class ImportWorkflowHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IPlanLimitService _planLimitService = Substitute.For<IPlanLimitService>();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ImportWorkflowHandler _handler;

    private readonly IWorkflowReferenceSync _referenceSync = Substitute.For<IWorkflowReferenceSync>();

    public ImportWorkflowHandlerTests()
    {
        _planLimitService.EnsureWithinLimitAsync(Arg.Any<Guid>(), Arg.Any<PlanLimitResourceType>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _referenceSync
            .SyncAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowReferenceSyncResult(HasBrokenReferences: false));
        _handler = new ImportWorkflowHandler(_planLimitService, _repo, _referenceSync, _uow);
    }

    private static WorkflowExportDto BuildExportDto(string name = "Imported Workflow") =>
        new(
            name,
            "Description",
            [
                new StepExportDto(Guid.NewGuid(), "Start", StepType.Start, null),
                new StepExportDto(Guid.NewGuid(), "Review", StepType.Form, new Dictionary<string, object?> { ["form_id"] = "abc" }),
                new StepExportDto(Guid.NewGuid(), "End", StepType.End, null)
            ],
            [],
            [new TriggerExportDto(TriggerType.Manual, null)]);

    [Fact]
    public async Task Handle_WhenNameIsAvailable_CreatesNewDraftWorkflowAndSaves()
    {
        WorkflowExportDto exportDto = BuildExportDto();
        _repo.NameExistsAsync(exportDto.Name, OrgId, null, Arg.Any<CancellationToken>()).Returns(false);

        Result<Guid> result = await _handler.Handle(
            new ImportWorkflowCommand(OrgId, "user", exportDto), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).AddAsync(
            Arg.Is<WorkflowDefinition>(w =>
                w.Name == exportDto.Name &&
                w.Status == WorkflowStatus.Draft &&
                w.OrganizationId == OrgId),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ReturnsConflict()
    {
        WorkflowExportDto exportDto = BuildExportDto("Existing Workflow");
        _repo.NameExistsAsync(exportDto.Name, OrgId, null, Arg.Any<CancellationToken>()).Returns(true);

        Result<Guid> result = await _handler.Handle(
            new ImportWorkflowCommand(OrgId, "user", exportDto), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenImportHasStepsAndTriggers_ImportsThemCorrectly()
    {
        WorkflowExportDto exportDto = BuildExportDto();
        _repo.NameExistsAsync(Arg.Any<string>(), OrgId, null, Arg.Any<CancellationToken>()).Returns(false);
        WorkflowDefinition? addedWorkflow = null;
        await _repo.AddAsync(Arg.Do<WorkflowDefinition>(w => addedWorkflow = w), Arg.Any<CancellationToken>());

        await _handler.Handle(new ImportWorkflowCommand(OrgId, "user", exportDto), CancellationToken.None);

        addedWorkflow.Should().NotBeNull();
        addedWorkflow!.Steps.Should().Contain(s => s.Name == "Review" && s.Type == StepType.Form);
        addedWorkflow.Triggers.Should().ContainSingle(t => t.Type == TriggerType.Manual);
    }

    [Fact]
    public async Task Handle_WhenPlanLimitExceeded_ReturnsFailureWithoutPersistence()
    {
        WorkflowExportDto exportDto = BuildExportDto();
        _planLimitService.EnsureWithinLimitAsync(
                OrgId,
                PlanLimitResourceType.Workflows,
                1,
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure(ErrorCodes.PlanLimit, "Workflow limit reached."));

        Result<Guid> result = await _handler.Handle(
            new ImportWorkflowCommand(OrgId, "user", exportDto),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.PlanLimit);
        await _repo.DidNotReceive().AddAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>());
        await _referenceSync.DidNotReceive().SyncAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
