using axis.identity.events;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Messaging;
using FluentAssertions;
using NSubstitute;
using Wolverine;

namespace Axis.Identity.Infrastructure.Tests.Messaging;

public sealed class WorkspaceProvisioningCoordinatorTests
{
    private readonly IWorkspaceRepository _WorkspaceRepository = Substitute.For<IWorkspaceRepository>();
    private readonly IWorkspaceModuleProvisioningRepository _provisioningRepository =
        Substitute.For<IWorkspaceModuleProvisioningRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();
    private readonly IPlatformProvisioningAlert _alert = Substitute.For<IPlatformProvisioningAlert>();

    [Fact]
    public async Task HandleReportAsync_WhenProvisioningFailsBeforeMaxAttempt_SchedulesRetry()
    {
        Workspace Workspace = CreateProvisioningWorkspace();
        WorkspaceModuleProvisioning row = WorkspaceModuleProvisioning.CreatePending(
            Workspace.Id,
            WorkspaceModuleNames.DataModeling);
        WorkspaceModuleProvisionReportEvent report = WorkspaceModuleProvisionReportEventFactory.Create(
            Workspace.Id,
            WorkspaceModuleNames.DataModeling,
            succeeded: false,
            attempt: 1,
            errorMessage: "db timeout");

        _WorkspaceRepository.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(Workspace);
        _provisioningRepository.GetAsync(
                Workspace.Id,
                WorkspaceModuleNames.DataModeling,
                Arg.Any<CancellationToken>())
            .Returns(row);

        await WorkspaceProvisioningCoordinator.HandleReportAsync(
            report,
            _WorkspaceRepository,
            _provisioningRepository,
            _unitOfWork,
            _messageBus,
            _alert,
            CancellationToken.None);

        row.Status.Should().Be(WorkspaceModuleProvisioningStatus.Pending);
        row.AttemptCount.Should().Be(2);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<RetryWorkspaceModuleProvisionMessage>(message =>
                message.workspaceId == Workspace.Id
                && message.Module == WorkspaceModuleNames.DataModeling
                && message.Attempt == 2),
            Arg.Any<DeliveryOptions>());
        await _alert.DidNotReceive().AlertProvisioningFailedAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleReportAsync_WhenAllModulesSucceeded_CompletesWorkspaceProvisioning()
    {
        Workspace Workspace = CreateProvisioningWorkspace();
        WorkspaceModuleProvisioning row = WorkspaceModuleProvisioning.CreatePending(
            Workspace.Id,
            WorkspaceModuleNames.DataModeling);
        List<WorkspaceModuleProvisioning> allRows =
        [
            row,
            CreateSucceededRow(Workspace.Id, WorkspaceModuleNames.FormBuilder),
            CreateSucceededRow(Workspace.Id, WorkspaceModuleNames.WorkflowBuilder),
            CreateSucceededRow(Workspace.Id, WorkspaceModuleNames.WorkflowEngine),
        ];

        WorkspaceModuleProvisionReportEvent report = WorkspaceModuleProvisionReportEventFactory.Create(
            Workspace.Id,
            WorkspaceModuleNames.DataModeling,
            succeeded: true,
            attempt: 1);

        _WorkspaceRepository.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(Workspace);
        _provisioningRepository.GetAsync(
                Workspace.Id,
                WorkspaceModuleNames.DataModeling,
                Arg.Any<CancellationToken>())
            .Returns(row);
        _provisioningRepository.GetAllForWorkspaceAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(allRows);

        await WorkspaceProvisioningCoordinator.HandleReportAsync(
            report,
            _WorkspaceRepository,
            _provisioningRepository,
            _unitOfWork,
            _messageBus,
            _alert,
            CancellationToken.None);

        row.Status.Should().Be(WorkspaceModuleProvisioningStatus.Succeeded);
        Workspace.Status.Should().Be(WorkspaceStatus.Active);
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleReportAsync_WhenProvisioningFailsAtMaxAttempt_MarksFailedAndAlerts()
    {
        Workspace Workspace = CreateProvisioningWorkspace();
        WorkspaceModuleProvisioning row = WorkspaceModuleProvisioning.CreatePending(
            Workspace.Id,
            WorkspaceModuleNames.DataModeling);
        WorkspaceModuleProvisionReportEvent report = WorkspaceModuleProvisionReportEventFactory.Create(
            Workspace.Id,
            WorkspaceModuleNames.DataModeling,
            succeeded: false,
            attempt: WorkspaceProvisioningCoordinator.MaxAttempts,
            errorMessage: "fatal");

        _WorkspaceRepository.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(Workspace);
        _provisioningRepository.GetAsync(
                Workspace.Id,
                WorkspaceModuleNames.DataModeling,
                Arg.Any<CancellationToken>())
            .Returns(row);
        _provisioningRepository.GetAllForWorkspaceAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns([row]);

        await WorkspaceProvisioningCoordinator.HandleReportAsync(
            report,
            _WorkspaceRepository,
            _provisioningRepository,
            _unitOfWork,
            _messageBus,
            _alert,
            CancellationToken.None);

        row.Status.Should().Be(WorkspaceModuleProvisioningStatus.Failed);
        row.AttemptCount.Should().Be(WorkspaceProvisioningCoordinator.MaxAttempts);
        Workspace.Status.Should().Be(WorkspaceStatus.ProvisioningFailed);
        await _alert.Received(1).AlertProvisioningFailedAsync(
            Workspace.Id,
            WorkspaceModuleNames.DataModeling,
            WorkspaceProvisioningCoordinator.MaxAttempts,
            "fatal",
            Arg.Any<CancellationToken>());
    }

    private static Workspace CreateProvisioningWorkspace()
    {
        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value!,
            Email.Create("admin@acme.com").Value!,
            WellKnownSubscriptionPlans.FreeId);
        Workspace.BeginProvisioning();
        return Workspace;
    }

    private static WorkspaceModuleProvisioning CreateSucceededRow(Guid workspaceId, string module)
    {
        WorkspaceModuleProvisioning row = WorkspaceModuleProvisioning.CreatePending(workspaceId, module);
        row.RecordSuccess();
        return row;
    }
}
