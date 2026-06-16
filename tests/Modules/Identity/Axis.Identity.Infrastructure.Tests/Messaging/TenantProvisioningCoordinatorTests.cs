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

public sealed class TenantProvisioningCoordinatorTests
{
    private readonly ITenantRepository _TenantRepository = Substitute.For<ITenantRepository>();
    private readonly ITenantModuleProvisioningRepository _provisioningRepository =
        Substitute.For<ITenantModuleProvisioningRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();
    private readonly IPlatformProvisioningAlert _alert = Substitute.For<IPlatformProvisioningAlert>();

    [Fact]
    public async Task HandleReportAsync_WhenProvisioningFailsBeforeMaxAttempt_SchedulesRetry()
    {
        Tenant Tenant = CreateProvisioningTenant();
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(
            Tenant.Id,
            TenantModuleNames.DataModeling);
        TenantModuleProvisionReportEvent report = TenantModuleProvisionReportEventFactory.Create(
            Tenant.Id,
            TenantModuleNames.DataModeling,
            succeeded: false,
            attempt: 1,
            errorMessage: "db timeout");

        _TenantRepository.GetByIdAsync(Tenant.Id, Arg.Any<CancellationToken>())
            .Returns(Tenant);
        _provisioningRepository.GetAsync(
                Tenant.Id,
                TenantModuleNames.DataModeling,
                Arg.Any<CancellationToken>())
            .Returns(row);

        await TenantProvisioningCoordinator.HandleReportAsync(
            report,
            _TenantRepository,
            _provisioningRepository,
            _unitOfWork,
            _messageBus,
            _alert,
            CancellationToken.None);

        row.Status.Should().Be(TenantModuleProvisioningStatus.Pending);
        row.AttemptCount.Should().Be(2);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<RetryTenantModuleProvisionMessage>(message =>
                message.tenantId == Tenant.Id
                && message.Module == TenantModuleNames.DataModeling
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
    public async Task HandleReportAsync_WhenAllModulesSucceeded_CompletesTenantProvisioning()
    {
        Tenant Tenant = CreateProvisioningTenant();
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(
            Tenant.Id,
            TenantModuleNames.DataModeling);
        List<TenantModuleProvisioning> allRows =
        [
            row,
            CreateSucceededRow(Tenant.Id, TenantModuleNames.FormBuilder),
            CreateSucceededRow(Tenant.Id, TenantModuleNames.WorkflowBuilder),
            CreateSucceededRow(Tenant.Id, TenantModuleNames.WorkflowEngine),
        ];

        TenantModuleProvisionReportEvent report = TenantModuleProvisionReportEventFactory.Create(
            Tenant.Id,
            TenantModuleNames.DataModeling,
            succeeded: true,
            attempt: 1);

        _TenantRepository.GetByIdAsync(Tenant.Id, Arg.Any<CancellationToken>())
            .Returns(Tenant);
        _provisioningRepository.GetAsync(
                Tenant.Id,
                TenantModuleNames.DataModeling,
                Arg.Any<CancellationToken>())
            .Returns(row);
        _provisioningRepository.GetAllForTenantAsync(Tenant.Id, Arg.Any<CancellationToken>())
            .Returns(allRows);

        await TenantProvisioningCoordinator.HandleReportAsync(
            report,
            _TenantRepository,
            _provisioningRepository,
            _unitOfWork,
            _messageBus,
            _alert,
            CancellationToken.None);

        row.Status.Should().Be(TenantModuleProvisioningStatus.Succeeded);
        Tenant.Status.Should().Be(TenantStatus.Active);
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleReportAsync_WhenProvisioningFailsAtMaxAttempt_MarksFailedAndAlerts()
    {
        Tenant Tenant = CreateProvisioningTenant();
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(
            Tenant.Id,
            TenantModuleNames.DataModeling);
        TenantModuleProvisionReportEvent report = TenantModuleProvisionReportEventFactory.Create(
            Tenant.Id,
            TenantModuleNames.DataModeling,
            succeeded: false,
            attempt: TenantProvisioningCoordinator.MaxAttempts,
            errorMessage: "fatal");

        _TenantRepository.GetByIdAsync(Tenant.Id, Arg.Any<CancellationToken>())
            .Returns(Tenant);
        _provisioningRepository.GetAsync(
                Tenant.Id,
                TenantModuleNames.DataModeling,
                Arg.Any<CancellationToken>())
            .Returns(row);
        _provisioningRepository.GetAllForTenantAsync(Tenant.Id, Arg.Any<CancellationToken>())
            .Returns([row]);

        await TenantProvisioningCoordinator.HandleReportAsync(
            report,
            _TenantRepository,
            _provisioningRepository,
            _unitOfWork,
            _messageBus,
            _alert,
            CancellationToken.None);

        row.Status.Should().Be(TenantModuleProvisioningStatus.Failed);
        row.AttemptCount.Should().Be(TenantProvisioningCoordinator.MaxAttempts);
        Tenant.Status.Should().Be(TenantStatus.ProvisioningFailed);
        await _alert.Received(1).AlertProvisioningFailedAsync(
            Tenant.Id,
            TenantModuleNames.DataModeling,
            TenantProvisioningCoordinator.MaxAttempts,
            "fatal",
            Arg.Any<CancellationToken>());
    }

    private static Tenant CreateProvisioningTenant()
    {
        Tenant Tenant = Tenant.Create(
            "Acme",
            TenantSlug.Create("acme").Value!,
            Email.Create("admin@acme.com").Value!,
            WellKnownSubscriptionPlans.FreeId);
        Tenant.BeginProvisioning();
        return Tenant;
    }

    private static TenantModuleProvisioning CreateSucceededRow(Guid tenantId, string module)
    {
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(tenantId, module);
        row.RecordSuccess();
        return row;
    }
}
