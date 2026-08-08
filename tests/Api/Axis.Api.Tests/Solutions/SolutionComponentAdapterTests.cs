using System.Text;
using Axis.Api.Solutions;
using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Contracts;
using Axis.Rules.Contracts;
using Axis.Solutions.Application;
using Axis.Solutions.Domain;
using FluentAssertions;
using IdentitySubjectKind = Axis.Identity.Contracts.SubjectKind;
using IdentitySubjectReference = Axis.Identity.Contracts.SubjectReference;

namespace Axis.Api.Tests.Solutions;

public sealed class SolutionComponentAdapterTests
{
    [Fact]
    public async Task RulesAdapter_WhenComponentIsValid_MapsExactReceipt()
    {
        RuleInstaller installer = new();
        RuleBindingSolutionAdapter adapter = new(installer);
        Guid workspaceId = Guid.NewGuid();
        SolutionApplyReceipt receipt = Receipt();

        await adapter.PreflightAsync(
            workspaceId,
            RuleComponent(),
            TestContext.Current.CancellationToken);
        await adapter.ApplyAsync(
            workspaceId,
            RuleComponent(),
            receipt,
            TestContext.Current.CancellationToken);

        installer.InstalledComponent.Should().NotBeNull();
        installer.InstalledComponent!.ComponentKey.Should().Be(RuleComponent().Key);
        installer.Receipt.Should().NotBeNull();
        installer.Receipt!.SolutionVersionId.Should().Be(receipt.SolutionVersionId);
        installer.Receipt.Actor.Kind.Should().Be(IdentitySubjectKind.Human);
        installer.Receipt.Actor.Id.Should().Be(receipt.ActorSubjectId);
        installer.Receipt.ComponentHash.Should().Be(receipt.ComponentSha256);
        installer.Receipt.OperationId.Should().Be(receipt.OperationId);
        installer.Receipt.StepId.Should().Be(receipt.StepId);
        installer.Receipt.LeaseEpoch.Should().Be(receipt.LeaseEpoch);
    }

    [Fact]
    public async Task RulesAdapter_WhenComponentHasUnknownProperty_BlocksPreflight()
    {
        RuleBindingSolutionAdapter adapter = new(new RuleInstaller());
        SolutionAdapterPreflight component = RuleComponent() with
        {
            Content = Encoding.UTF8.GetBytes(
                """{"schemaVersion":1,"definitionKey":"field.required","definitionVersion":1,"targetType":"business-object-field","targetId":"invoice.amount","useCaseOrTrigger":"record-save","inputMappings":{"value":{"kind":"Context","contextKey":"record.value","literalValues":[]}},"priority":0,"enabled":true,"failureBehavior":"FailClosed","unknown":true}"""),
        };

        Func<Task> act = () => adapter.PreflightAsync(
            Guid.NewGuid(),
            component,
            TestContext.Current.CancellationToken);

        SolutionAdapterException exception = (await act.Should().ThrowAsync<SolutionAdapterException>()).Which;
        exception.ProblemCode.Should().Be("rules.binding_component_invalid");
        exception.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task RulesAdapter_WhenJsonHasWhitespace_BlocksPreflight()
    {
        RuleBindingSolutionAdapter adapter = new(new RuleInstaller());
        SolutionAdapterPreflight component = RuleComponent() with
        {
            Content = Encoding.UTF8.GetBytes(
                """{ "schemaVersion":1,"definitionKey":"field.required","definitionVersion":1,"targetType":"business-object-field","targetId":"invoice.amount","useCaseOrTrigger":"record-save","inputMappings":{"value":{"kind":"Context","contextKey":"record.value","literalValues":[]}},"priority":0,"enabled":true,"failureBehavior":"FailClosed"}"""),
        };

        Func<Task> act = () => adapter.PreflightAsync(
            Guid.NewGuid(),
            component,
            TestContext.Current.CancellationToken);

        SolutionAdapterException exception = (await act.Should().ThrowAsync<SolutionAdapterException>()).Which;
        exception.ProblemCode.Should().Be("rules.binding_component_invalid");
    }

    [Fact]
    public async Task AuthorizationAdapter_WhenSolutionVersionDiffers_ReportsReadbackMismatch()
    {
        SolutionApplyReceipt receipt = Receipt();
        PolicyInstaller installer = new()
        {
            ReadBack = new(
                Guid.Parse("11111111-1111-4111-8111-111111111111"),
                receipt.SolutionVersionId,
                "9.9.9",
                receipt.ComponentSha256,
                receipt.OperationId.ToString("N"),
                receipt.StepId.ToString("N"),
                receipt.LeaseEpoch),
        };
        AuthorizationPolicySolutionAdapter adapter = new(installer);

        SolutionAdapterReadback result = await adapter.ReadBackAsync(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            new(AuthorizationPolicySolutionAdapter.Type, "policy", [], []),
            receipt,
            TestContext.Current.CancellationToken);

        result.Should().Be(new SolutionAdapterReadback(
            false,
            true,
            "authorization.policy_readback_mismatch"));
    }

    [Fact]
    public async Task BusinessObjectAdapter_WhenComponentIsValid_MapsExactContentAndReceipt()
    {
        BusinessObjectInstaller installer = new();
        BusinessObjectDefinitionSolutionAdapter adapter = new(installer);
        Guid workspaceId = Guid.NewGuid();
        SolutionApplyReceipt receipt = Receipt();

        await adapter.PreflightAsync(
            workspaceId,
            BusinessObjectComponent(),
            TestContext.Current.CancellationToken);
        await adapter.ApplyAsync(
            workspaceId,
            BusinessObjectComponent(),
            receipt,
            TestContext.Current.CancellationToken);

        installer.InstalledComponent.Should().NotBeNull();
        installer.InstalledComponent!.ObjectKey.Should().Be("invoice");
        installer.InstalledComponent.Fields.Single().BindingKeys.Should().Equal(RuleComponent().Key);
        installer.Receipt.Should().NotBeNull();
        installer.Receipt!.SolutionVersionId.Should().Be(receipt.SolutionVersionId);
        installer.Receipt.Actor.Should().Be(IdentitySubjectReference.Human(receipt.ActorSubjectId));
        installer.Receipt.ComponentHash.Should().Be(receipt.ComponentSha256);
        installer.Receipt.OperationId.Should().Be(receipt.OperationId);
        installer.Receipt.StepId.Should().Be(receipt.StepId);
        installer.Receipt.LeaseEpoch.Should().Be(receipt.LeaseEpoch);
    }

    [Fact]
    public async Task BusinessObjectAdapter_WhenBindingDependencyIsMissing_BlocksPreflight()
    {
        BusinessObjectDefinitionSolutionAdapter adapter = new(new BusinessObjectInstaller());
        SolutionAdapterPreflight component = BusinessObjectComponent() with { DependsOn = [] };

        Func<Task> act = () => adapter.PreflightAsync(
            Guid.NewGuid(),
            component,
            TestContext.Current.CancellationToken);

        SolutionAdapterException exception = (await act.Should().ThrowAsync<SolutionAdapterException>()).Which;
        exception.ProblemCode.Should().Be("businessObjects.definition_component_invalid");
        exception.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task BusinessObjectAdapter_WhenStoredContentDiffers_ReportsReadbackMismatch()
    {
        Guid workspaceId = Guid.NewGuid();
        SolutionApplyReceipt receipt = Receipt();
        BusinessObjectInstaller installer = new()
        {
            ReadBack = new(
                workspaceId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "invoice",
                BusinessObjectContractComponent("Changed"),
                receipt.SolutionVersionId,
                receipt.ComponentSha256,
                receipt.OperationId,
                receipt.StepId,
                receipt.LeaseEpoch),
        };

        SolutionAdapterReadback result = await new BusinessObjectDefinitionSolutionAdapter(installer)
            .ReadBackAsync(
                workspaceId,
                BusinessObjectComponent(),
                receipt,
                TestContext.Current.CancellationToken);

        result.Should().Be(new SolutionAdapterReadback(
            false,
            true,
            "businessObjects.definition_readback_mismatch"));
    }

    private static SolutionAdapterPreflight RuleComponent() =>
        new(
            RuleBindingSolutionAdapter.Type,
            "field.required@1:business-object-field:invoice.amount:record-save",
            Encoding.UTF8.GetBytes(
                """{"schemaVersion":1,"definitionKey":"field.required","definitionVersion":1,"targetType":"business-object-field","targetId":"invoice.amount","useCaseOrTrigger":"record-save","inputMappings":{"value":{"kind":"Context","contextKey":"record.value","literalValues":[]}},"priority":0,"enabled":true,"failureBehavior":"FailClosed"}"""),
            []);

    private static SolutionAdapterPreflight BusinessObjectComponent() =>
        new(
            BusinessObjectDefinitionSolutionAdapter.Type,
            "invoice",
            Encoding.UTF8.GetBytes(
                """{"schemaVersion":1,"objectKey":"invoice","name":"Invoice","fields":[{"fieldKey":"amount","label":"Amount","order":0,"fieldType":"Decimal","bindingKeys":["field.required@1:business-object-field:invoice.amount:record-save"]}]}"""),
            [new(RuleBindingSolutionAdapter.Type, RuleComponent().Key)]);

    private static BusinessObjectDefinitionSolutionComponent BusinessObjectContractComponent(string name) =>
        new(
            "invoice",
            "invoice",
            name,
            [
                new(
                    "amount",
                    "Amount",
                    0,
                    BusinessObjectSolutionFieldType.Decimal,
                    null,
                    [RuleComponent().Key]),
            ]);

    private static SolutionApplyReceipt Receipt() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SolutionSubjectKind.Human,
            "test-correlation",
            "1.0.0",
            new string('a', 64),
            3);

    private sealed class RuleInstaller : IRuleBindingSolutionInstaller
    {
        public RuleBindingSolutionComponent? InstalledComponent { get; private set; }
        public RuleBindingInstallationReceipt? Receipt { get; private set; }

        public Task<RuleBindingInstallationResult> ValidateAsync(
            Guid workspaceId,
            RuleBindingSolutionComponent component,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RuleBindingInstallationResult(true));

        public Task<RuleBindingInstallationResult> InstallAsync(
            Guid workspaceId,
            RuleBindingSolutionComponent component,
            RuleBindingInstallationReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            InstalledComponent = component;
            Receipt = receipt;
            return Task.FromResult(new RuleBindingInstallationResult(true));
        }

        public Task<RuleBindingInstallationReadBack?> ReadBackAsync(
            Guid workspaceId,
            string componentKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RuleBindingInstallationReadBack?>(null);
    }

    private sealed class PolicyInstaller : IProductPolicyInstaller
    {
        public ProductPolicyComponentReadBack? ReadBack { get; init; }

        public ProductPolicyInstallResult Validate(ProductPolicyComponent component) => new(true);

        public Task<ProductPolicyInstallResult> InstallAsync(
            InstallProductPolicyRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductPolicyInstallResult(true));

        public Task<ProductPolicyComponentReadBack?> ReadBackAsync(
            Guid workspaceId,
            Guid versionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ReadBack);
    }

    private sealed class BusinessObjectInstaller : IBusinessObjectDefinitionSolutionInstaller
    {
        public BusinessObjectDefinitionSolutionComponent? InstalledComponent { get; private set; }
        public BusinessObjectDefinitionInstallationReceipt? Receipt { get; private set; }
        public BusinessObjectDefinitionInstallationReadBack? ReadBack { get; init; }

        public Task<BusinessObjectDefinitionInstallationResult> ValidateAsync(
            Guid workspaceId,
            BusinessObjectDefinitionSolutionComponent component,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BusinessObjectDefinitionInstallationResult(true));

        public Task<BusinessObjectDefinitionInstallationResult> InstallAsync(
            Guid workspaceId,
            BusinessObjectDefinitionSolutionComponent component,
            BusinessObjectDefinitionInstallationReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            InstalledComponent = component;
            Receipt = receipt;
            return Task.FromResult(new BusinessObjectDefinitionInstallationResult(true));
        }

        public Task<BusinessObjectDefinitionInstallationReadBack?> ReadBackAsync(
            Guid workspaceId,
            string componentKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ReadBack);
    }
}
