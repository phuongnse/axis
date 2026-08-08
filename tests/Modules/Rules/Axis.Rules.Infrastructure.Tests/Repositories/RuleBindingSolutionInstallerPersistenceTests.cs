using Axis.Identity.Contracts;
using Axis.Rules.Application;
using Axis.Rules.Contracts;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Rules.Infrastructure.Repositories;
using Axis.Rules.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace Axis.Rules.Infrastructure.Tests.Repositories;

[Collection("RulesDb")]
public sealed class RuleBindingSolutionInstallerPersistenceTests(RulesDatabaseFixture db)
{
    [Fact]
    public async Task InstallAsync_WhenEpochAdvances_PersistsCanonicalReceipt()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleBindingSolutionComponent component = Component($"invoice{Guid.NewGuid():N}"[..20]);
        RuleBindingInstallationReceipt receipt = Receipt(1);

        await using (RulesDbContext firstContext = db.CreateContext())
        {
            RuleBindingSolutionInstaller installer = Installer(firstContext);
            (await installer.InstallAsync(
                workspaceId,
                component,
                receipt,
                TestContext.Current.CancellationToken)).IsSuccess.Should().BeTrue();
        }

        await using (RulesDbContext advanceContext = db.CreateContext())
        {
            RuleBindingSolutionInstaller installer = Installer(advanceContext);
            (await installer.InstallAsync(
                workspaceId,
                component,
                receipt with { LeaseEpoch = 2 },
                TestContext.Current.CancellationToken)).IsSuccess.Should().BeTrue();
        }

        await using RulesDbContext readContext = db.CreateContext();
        RuleBindingInstallationReadBack? readBack = await Installer(readContext).ReadBackAsync(
            workspaceId,
            component.ComponentKey,
            TestContext.Current.CancellationToken);

        readBack.Should().NotBeNull();
        readBack!.SolutionVersionId.Should().Be(receipt.SolutionVersionId);
        readBack.ComponentHash.Should().Be(receipt.ComponentHash);
        readBack.OperationId.Should().Be(receipt.OperationId);
        readBack.StepId.Should().Be(receipt.StepId);
        readBack.LeaseEpoch.Should().Be(2);
    }

    [Fact]
    public async Task InstallAsync_WhenReceiptIsStale_PreservesHigherEpoch()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleBindingSolutionComponent component = Component($"order{Guid.NewGuid():N}"[..18]);
        RuleBindingInstallationReceipt receipt = Receipt(2);

        await using (RulesDbContext firstContext = db.CreateContext())
        {
            (await Installer(firstContext).InstallAsync(
                workspaceId,
                component,
                receipt,
                TestContext.Current.CancellationToken)).IsSuccess.Should().BeTrue();
        }

        await using (RulesDbContext staleContext = db.CreateContext())
        {
            RuleBindingInstallationResult stale = await Installer(staleContext).InstallAsync(
                workspaceId,
                component,
                receipt with { LeaseEpoch = 1 },
                TestContext.Current.CancellationToken);
            stale.Should().Be(new RuleBindingInstallationResult(false, "rules.binding_install_stale_receipt"));
        }

        await using RulesDbContext readContext = db.CreateContext();
        RuleBindingInstallationReadBack? readBack = await Installer(readContext).ReadBackAsync(
            workspaceId,
            component.ComponentKey,
            TestContext.Current.CancellationToken);
        readBack!.LeaseEpoch.Should().Be(2);
    }

    private static RuleBindingSolutionInstaller Installer(RulesDbContext context) =>
        new(new RuleBindingRepository(context), new RulesUnitOfWork(context), TimeProvider.System);

    private static RuleBindingSolutionComponent Component(string objectKey)
    {
        string targetId = $"{objectKey}.amount";
        return new(
            $"field.required@1:business-object-field:{targetId}:record-save",
            "field.required",
            1,
            "business-object-field",
            targetId,
            "record-save",
            new Dictionary<string, RuleInputMappingDto>
            {
                ["value"] = new(RuleInputMappingKind.Context, "record.value", []),
            },
            0,
            true,
            RuleBindingFailureBehavior.FailClosed);
    }

    private static RuleBindingInstallationReceipt Receipt(long leaseEpoch) =>
        new(
            Guid.NewGuid(),
            SubjectReference.Service(Guid.NewGuid()),
            new string('b', 64),
            Guid.NewGuid(),
            Guid.NewGuid(),
            leaseEpoch);
}
