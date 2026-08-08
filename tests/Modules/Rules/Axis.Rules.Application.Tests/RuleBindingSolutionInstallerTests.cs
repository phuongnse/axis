using Axis.Identity.Contracts;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests;

public sealed class RuleBindingSolutionInstallerTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SolutionVersionId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ActorId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid OperationId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid StepId = Guid.Parse("55555555-5555-4555-8555-555555555555");

    [Fact]
    public async Task Install_WhenReceiptEpochAdvances_UpdatesOnlyFencingReceipt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (RuleBindingSolutionInstaller installer, IRuleBindingRepository repository, _) = CreateInstaller();
        RuleBinding? stored = null;
        repository.AddAsync(Arg.Do<RuleBinding>(binding => stored = binding), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        repository.GetByIdentityForWorkspaceAsync(
                Arg.Any<Guid>(), Arg.Any<RuleDefinitionKey>(), Arg.Any<int>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => stored);
        repository.GetInstalledByComponentKeyAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => stored);

        RuleBindingInstallationResult first = await installer.InstallAsync(
            WorkspaceId, Component(), Receipt(1), cancellationToken);
        RuleBindingInstallationResult advanced = await installer.InstallAsync(
            WorkspaceId, Component(), Receipt(2), cancellationToken);
        RuleBindingInstallationReadBack? readBack = await installer.ReadBackAsync(
            WorkspaceId, Component().ComponentKey, cancellationToken);

        first.IsSuccess.Should().BeTrue();
        advanced.IsSuccess.Should().BeTrue();
        stored.Should().NotBeNull();
        stored!.Revision.Should().Be(1);
        readBack.Should().NotBeNull();
        readBack!.LeaseEpoch.Should().Be(2);
        readBack.OperationId.Should().Be(OperationId);
    }

    [Fact]
    public async Task Install_WhenReceiptIsStale_ReturnsStableProblemWithoutSaving()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (RuleBindingSolutionInstaller installer, IRuleBindingRepository repository, IUnitOfWork unitOfWork) =
            CreateInstaller();
        RuleBinding? stored = null;
        repository.AddAsync(Arg.Do<RuleBinding>(binding => stored = binding), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        repository.GetByIdentityForWorkspaceAsync(
                Arg.Any<Guid>(), Arg.Any<RuleDefinitionKey>(), Arg.Any<int>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => stored);

        (await installer.InstallAsync(WorkspaceId, Component(), Receipt(2), cancellationToken)).IsSuccess.Should().BeTrue();
        unitOfWork.ClearReceivedCalls();
        RuleBindingInstallationResult stale = await installer.InstallAsync(
            WorkspaceId, Component(), Receipt(1), cancellationToken);

        stale.Should().Be(new RuleBindingInstallationResult(false, "rules.binding_install_stale_receipt"));
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validate_WhenDefinitionIsNotBuiltIn_RejectsComponent()
    {
        (RuleBindingSolutionInstaller installer, _, _) = CreateInstaller();
        RuleBindingSolutionComponent component = Component() with
        {
            ComponentKey = "custom.rule@1:business-object-field:invoice.amount:record-save",
            DefinitionKey = "custom.rule",
        };

        RuleBindingInstallationResult result = await installer.ValidateAsync(
            WorkspaceId,
            component,
            TestContext.Current.CancellationToken);

        result.Should().Be(new RuleBindingInstallationResult(false, "rules.binding_component_invalid"));
    }

    private static (RuleBindingSolutionInstaller Installer, IRuleBindingRepository Repository, IUnitOfWork UnitOfWork)
        CreateInstaller()
    {
        IRuleBindingRepository repository = Substitute.For<IRuleBindingRepository>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        return (new(repository, unitOfWork, TimeProvider.System), repository, unitOfWork);
    }

    private static RuleBindingSolutionComponent Component() =>
        new(
            "field.required@1:business-object-field:invoice.amount:record-save",
            "field.required",
            1,
            "business-object-field",
            "invoice.amount",
            "record-save",
            new Dictionary<string, RuleInputMappingDto>
            {
                ["value"] = new(Axis.Rules.Contracts.RuleInputMappingKind.Context, "record.value", []),
            },
            0,
            true,
            Axis.Rules.Contracts.RuleBindingFailureBehavior.FailClosed);

    private static RuleBindingInstallationReceipt Receipt(long leaseEpoch) =>
        new(
            SolutionVersionId,
            SubjectReference.Service(ActorId),
            new string('a', 64),
            OperationId,
            StepId,
            leaseEpoch);
}
