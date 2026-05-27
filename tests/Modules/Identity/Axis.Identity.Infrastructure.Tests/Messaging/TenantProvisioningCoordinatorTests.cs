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
    private readonly IOrganizationRepository _organizationRepository = Substitute.For<IOrganizationRepository>();
    private readonly ITenantModuleProvisioningRepository _provisioningRepository =
        Substitute.For<ITenantModuleProvisioningRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();
    private readonly IPlatformProvisioningAlert _alert = Substitute.For<IPlatformProvisioningAlert>();

    [Fact]
    public async Task HandleReportAsync_WhenProvisioningFailsBeforeMaxAttempt_SchedulesRetry()
    {
        Organization organization = CreateProvisioningOrganization();
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(
            organization.Id,
            TenantModuleNames.DataModeling);
        TenantModuleProvisionReportEvent report = TenantModuleProvisionReportEventFactory.Create(
            organization.Id,
            TenantModuleNames.DataModeling,
            succeeded: false,
            attempt: 1,
            errorMessage: "db timeout");

        _organizationRepository.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(organization);
        _provisioningRepository.GetAsync(
                organization.Id,
                TenantModuleNames.DataModeling,
                Arg.Any<CancellationToken>())
            .Returns(row);

        await TenantProvisioningCoordinator.HandleReportAsync(
            report,
            _organizationRepository,
            _provisioningRepository,
            _unitOfWork,
            _messageBus,
            _alert,
            CancellationToken.None);

        row.Status.Should().Be(TenantModuleProvisioningStatus.Pending);
        row.AttemptCount.Should().Be(2);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _alert.DidNotReceive().AlertProvisioningFailedAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleReportAsync_WhenAllModulesSucceeded_CompletesOrganizationProvisioning()
    {
        Organization organization = CreateProvisioningOrganization();
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(
            organization.Id,
            TenantModuleNames.DataModeling);
        List<TenantModuleProvisioning> allRows =
        [
            row,
            CreateSucceededRow(organization.Id, TenantModuleNames.FormBuilder),
            CreateSucceededRow(organization.Id, TenantModuleNames.WorkflowBuilder),
            CreateSucceededRow(organization.Id, TenantModuleNames.WorkflowEngine),
        ];

        TenantModuleProvisionReportEvent report = TenantModuleProvisionReportEventFactory.Create(
            organization.Id,
            TenantModuleNames.DataModeling,
            succeeded: true,
            attempt: 1);

        _organizationRepository.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(organization);
        _provisioningRepository.GetAsync(
                organization.Id,
                TenantModuleNames.DataModeling,
                Arg.Any<CancellationToken>())
            .Returns(row);
        _provisioningRepository.GetAllForOrganizationAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(allRows);

        await TenantProvisioningCoordinator.HandleReportAsync(
            report,
            _organizationRepository,
            _provisioningRepository,
            _unitOfWork,
            _messageBus,
            _alert,
            CancellationToken.None);

        row.Status.Should().Be(TenantModuleProvisioningStatus.Succeeded);
        organization.Status.Should().Be(OrganizationStatus.Active);
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleReportAsync_WhenProvisioningFailsAtMaxAttempt_MarksFailedAndAlerts()
    {
        Organization organization = CreateProvisioningOrganization();
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(
            organization.Id,
            TenantModuleNames.DataModeling);
        TenantModuleProvisionReportEvent report = TenantModuleProvisionReportEventFactory.Create(
            organization.Id,
            TenantModuleNames.DataModeling,
            succeeded: false,
            attempt: TenantProvisioningCoordinator.MaxAttempts,
            errorMessage: "fatal");

        _organizationRepository.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(organization);
        _provisioningRepository.GetAsync(
                organization.Id,
                TenantModuleNames.DataModeling,
                Arg.Any<CancellationToken>())
            .Returns(row);
        _provisioningRepository.GetAllForOrganizationAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns([row]);

        await TenantProvisioningCoordinator.HandleReportAsync(
            report,
            _organizationRepository,
            _provisioningRepository,
            _unitOfWork,
            _messageBus,
            _alert,
            CancellationToken.None);

        row.Status.Should().Be(TenantModuleProvisioningStatus.Failed);
        row.AttemptCount.Should().Be(TenantProvisioningCoordinator.MaxAttempts);
        organization.Status.Should().Be(OrganizationStatus.ProvisioningFailed);
        await _alert.Received(1).AlertProvisioningFailedAsync(
            organization.Id,
            TenantModuleNames.DataModeling,
            TenantProvisioningCoordinator.MaxAttempts,
            "fatal",
            Arg.Any<CancellationToken>());
    }

    private static Organization CreateProvisioningOrganization()
    {
        Organization organization = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value!,
            Email.Create("admin@acme.com").Value!,
            WellKnownSubscriptionPlans.FreeId);
        organization.BeginProvisioning();
        return organization;
    }

    private static TenantModuleProvisioning CreateSucceededRow(Guid organizationId, string module)
    {
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(organizationId, module);
        row.RecordSuccess();
        return row;
    }
}
