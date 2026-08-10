using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Contracts;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Identity.Contracts;
using Axis.Rules.Contracts;
using FluentAssertions;
using NSubstitute;
using IdentitySubjectReference = Axis.Identity.Contracts.SubjectReference;

namespace Axis.BusinessObjects.Application.Tests;

public sealed class BusinessObjectDefinitionSolutionInstallerTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ActorId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid SolutionVersionId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid OperationId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid StepId = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid BindingId = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private const string BindingKey = "field.required@1:business-object-field:invoice.amount:record-save";

    [Fact]
    public async Task InstallAsync_WhenRuleDependencyMatches_PersistsPublishedDefinitionAndExactReadback()
    {
        InstallerContext context = CreateContext();

        BusinessObjectDefinitionInstallationResult result = await context.Installer.InstallAsync(
            WorkspaceId,
            Component(),
            Receipt(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        context.Added.Should().NotBeNull();
        context.Added!.Status.Should().Be(BusinessObjectDefinitionStatus.Published);
        context.Added.IsInstalled.Should().BeTrue();
        context.Added.Fields.Single().Rules.Single().BindingId.Should().Be(BindingId);
        context.Added.Fields.Single().Rules.Single().BindingRevision.Should().Be(7);
        context.Added.Fields.Single().Rules.Single().BindingKey.Should().Be(BindingKey);

        context.Definitions.GetInstalledByComponentKeyAsync(
                WorkspaceId,
                "invoice",
                Arg.Any<CancellationToken>())
            .Returns(context.Added);
        BusinessObjectDefinitionInstallationReadBack? readBack = await context.Installer.ReadBackAsync(
            WorkspaceId,
            "invoice",
            TestContext.Current.CancellationToken);

        readBack.Should().NotBeNull();
        readBack!.Component.Should().BeEquivalentTo(Component());
        readBack.ComponentHash.Should().Be(new string('a', 64));
        readBack.LeaseEpoch.Should().Be(3);
    }

    [Fact]
    public async Task InstallAsync_WhenReceiptRetries_AdvancesOnlyMonotonicEpoch()
    {
        InstallerContext context = CreateContext();
        (await context.Installer.InstallAsync(
            WorkspaceId,
            Component(),
            Receipt(),
            TestContext.Current.CancellationToken)).IsSuccess.Should().BeTrue();
        context.Definitions.GetByKeyForWorkspaceAsync(
                BusinessObjectDefinitionKey.Create("invoice").Value,
                WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(context.Added);

        BusinessObjectDefinitionInstallationResult exact = await context.Installer.InstallAsync(
            WorkspaceId,
            Component(),
            Receipt(),
            TestContext.Current.CancellationToken);
        BusinessObjectDefinitionInstallationResult advanced = await context.Installer.InstallAsync(
            WorkspaceId,
            Component(),
            Receipt() with { LeaseEpoch = 4 },
            TestContext.Current.CancellationToken);
        BusinessObjectDefinitionInstallationResult stale = await context.Installer.InstallAsync(
            WorkspaceId,
            Component(),
            Receipt(),
            TestContext.Current.CancellationToken);
        BusinessObjectDefinitionInstallationResult conflict = await context.Installer.InstallAsync(
            WorkspaceId,
            Component(),
            Receipt() with { ComponentHash = new string('c', 64), LeaseEpoch = 5 },
            TestContext.Current.CancellationToken);

        exact.IsSuccess.Should().BeTrue();
        advanced.IsSuccess.Should().BeTrue();
        context.Added!.InstalledLeaseEpoch.Should().Be(4);
        stale.ProblemCode.Should().Be("businessObjects.definition_install_stale_receipt");
        conflict.ProblemCode.Should().Be("businessObjects.definition_install_conflict");
        await context.UnitOfWork.Received(3).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallAsync_WhenOriginIsService_PreservesServicePublisher()
    {
        InstallerContext context = CreateContext();

        BusinessObjectDefinitionInstallationResult result = await context.Installer.InstallAsync(
            WorkspaceId,
            Component(),
            Receipt() with { Actor = IdentitySubjectReference.Service(ActorId) },
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        BusinessObjectDefinitionVersion version = context.Added!.Versions.Single();
        version.PublishedBySubject.Kind.Should().Be(
            Axis.BusinessObjects.Domain.ValueObjects.SubjectKind.Service);
        version.PublishedBySubject.Id.Should().Be(ActorId);
    }

    [Fact]
    public async Task InstallAsync_WhenRuleTargetDiffers_RejectsBeforeDefinitionMutation()
    {
        InstallerContext context = CreateContext(targetId: "invoice.other");

        BusinessObjectDefinitionInstallationResult result = await context.Installer.InstallAsync(
            WorkspaceId,
            Component(),
            Receipt(),
            TestContext.Current.CancellationToken);

        result.ProblemCode.Should().Be("businessObjects.definition_binding_unavailable");
        context.Added.Should().BeNull();
        await context.Definitions.DidNotReceive().AddAsync(
            Arg.Any<BusinessObjectDefinition>(),
            Arg.Any<CancellationToken>());
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static InstallerContext CreateContext(string targetId = "invoice.amount")
    {
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        IRuleBindingSolutionInstaller rules = Substitute.For<IRuleBindingSolutionInstaller>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        rules.ReadBackAsync(WorkspaceId, BindingKey, Arg.Any<CancellationToken>())
            .Returns(new RuleBindingInstallationReadBack(
                WorkspaceId,
                BindingId,
                7,
                BindingKey,
                new RuleBindingSolutionComponent(
                    BindingKey,
                    "field.required",
                    1,
                    "business-object-field",
                    targetId,
                    "record-save",
                    new Dictionary<string, RuleInputMappingDto>(StringComparer.Ordinal)
                    {
                        ["value"] = new(RuleInputMappingKind.Context, "record.value", []),
                    },
                    0,
                    true,
                    RuleBindingFailureBehavior.FailClosed),
                SolutionVersionId,
                new string('b', 64),
                OperationId,
                Guid.NewGuid(),
                3));

        InstallerContext context = new(
            definitions,
            unitOfWork,
            new BusinessObjectDefinitionSolutionInstaller(
                definitions,
                rules,
                unitOfWork,
                TimeProvider.System));
        definitions.AddAsync(Arg.Any<BusinessObjectDefinition>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                context.Added = call.Arg<BusinessObjectDefinition>();
                return Task.CompletedTask;
            });
        return context;
    }

    private static BusinessObjectDefinitionSolutionComponent Component() =>
        new(
            "invoice",
            "invoice",
            "Invoice",
            [
                new(
                    "amount",
                    "Amount",
                    0,
                    BusinessObjectSolutionFieldType.Decimal,
                    null,
                    [BindingKey]),
            ]);

    private static BusinessObjectDefinitionInstallationReceipt Receipt() =>
        new(
            SolutionVersionId,
            IdentitySubjectReference.Human(ActorId),
            new string('a', 64),
            OperationId,
            StepId,
            3);

    private sealed record InstallerContext(
        IBusinessObjectDefinitionRepository Definitions,
        IUnitOfWork UnitOfWork,
        BusinessObjectDefinitionSolutionInstaller Installer)
    {
        public BusinessObjectDefinition? Added { get; set; }
    }
}
