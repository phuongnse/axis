using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using FluentAssertions;
using NSubstitute;
using ContractRuleValueType = Axis.Rules.Contracts.RuleValueType;
using DomainFailureBehavior = Axis.Rules.Domain.RuleBindingFailureBehavior;

namespace Axis.Rules.Application.Tests;

public sealed class RuleBindingReferenceValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WhenRequiredRecordValueIsNotMapped_ReturnsStableContextFailure()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleBinding binding = CreateBinding(
            workspaceId,
            new Dictionary<string, RuleInputMapping>
            {
                ["value"] = RuleInputMapping.FromLiteral(["fixed"]).Value,
            });
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        bindings.GetByIdForWorkspaceAsync(binding.Id, workspaceId, Arg.Any<CancellationToken>()).Returns(binding);
        RuleBindingReferenceValidator sut = new(bindings, Substitute.For<IRuleDefinitionRepository>());

        RuleBindingReferenceValidationResult result = await sut.ValidateAsync(Request(workspaceId, binding.Id.Value), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("binding_required_context_unmapped");
    }

    [Fact]
    public async Task ValidateAsync_WhenContextKeyIsUnknown_ReturnsStableContextFailure()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleBinding binding = CreateBinding(workspaceId, ContextMapping("other.value"));
        RuleBindingReferenceValidator sut = CreateValidator(workspaceId, binding);

        RuleBindingReferenceValidationResult result = await sut.ValidateAsync(Request(workspaceId, binding.Id.Value), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("binding_context_key_not_found");
    }

    [Fact]
    public async Task ValidateAsync_WhenContextTypeDoesNotMatch_ReturnsStableContextFailure()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleBinding binding = CreateBinding(workspaceId, ContextMapping("record.value"), "field.numeric_range");
        RuleBindingReferenceValidator sut = CreateValidator(workspaceId, binding);

        RuleBindingReferenceValidationResult result = await sut.ValidateAsync(Request(workspaceId, binding.Id.Value), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("binding_context_type_mismatch");
    }

    [Fact]
    public async Task ValidateAsync_WhenScalarContextFeedsMultipleCapableInput_Succeeds()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleBinding binding = CreateBinding(workspaceId, ContextMapping("record.value"));
        RuleBindingReferenceValidator sut = CreateValidator(workspaceId, binding);

        RuleBindingReferenceValidationResult result = await sut.ValidateAsync(Request(workspaceId, binding.Id.Value), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.Revision.Should().Be(binding.Revision);
    }

    [Fact]
    public async Task ValidateAsync_WhenMultipleContextFeedsScalarInput_ReturnsStableCardinalityFailure()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleBinding binding = CreateBinding(
            workspaceId,
            new Dictionary<string, RuleInputMapping>
            {
                ["value"] = RuleInputMapping.FromContext("record.value").Value,
                ["min"] = RuleInputMapping.FromLiteral(["1"]).Value,
            },
            "field.numeric_range");
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        bindings.GetByIdForWorkspaceAsync(binding.Id, workspaceId, Arg.Any<CancellationToken>()).Returns(binding);
        RuleBindingReferenceValidator sut = new(bindings, Substitute.For<IRuleDefinitionRepository>());

        RuleBindingReferenceValidationResult result = await sut.ValidateAsync(Request(
            workspaceId,
            binding.Id.Value,
            ContractRuleValueType.Integer,
            allowMultiple: true), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("binding_context_cardinality_mismatch");
    }

    [Fact]
    public async Task ValidateAsync_WhenExactBindingRevisionChanges_ReturnsRevisionConflictBeforeMutation()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleBinding binding = CreateBinding(
            workspaceId,
            new Dictionary<string, RuleInputMapping>
            {
                ["value"] = RuleInputMapping.FromContext("record.value").Value,
            });
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        bindings.GetByIdForWorkspaceAsync(binding.Id, workspaceId, Arg.Any<CancellationToken>()).Returns(binding);
        RuleBindingReferenceValidator sut = new(bindings, Substitute.For<IRuleDefinitionRepository>());

        RuleBindingReferenceValidationResult result = await sut.ValidateAsync(Request(
            workspaceId,
            binding.Id.Value,
            expectedRevision: binding.Revision + 1), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("binding_revision_conflict");
    }

    [Fact]
    public async Task ValidateAsync_WhenChangedBindingIsNowDisabled_ReturnsRevisionConflictBeforeAvailabilityFailure()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleBinding binding = CreateBinding(workspaceId, ContextMapping("record.value"));
        int attachedRevision = binding.Revision;
        binding.Update(
            attachedRevision,
            binding.DefinitionKey,
            binding.DefinitionVersion,
            binding.TargetType,
            binding.TargetId,
            binding.UseCaseOrTrigger,
            binding.InputMappings,
            binding.Priority,
            enabled: false,
            failureBehavior: binding.FailureBehavior,
            updatedBySubject: RuleSubjectReference.Human(Guid.NewGuid()),
            updatedAt: DateTime.UtcNow).IsSuccess.Should().BeTrue();
        RuleBindingReferenceValidator sut = CreateValidator(workspaceId, binding);

        RuleBindingReferenceValidationResult result = await sut.ValidateAsync(
            Request(workspaceId, binding.Id.Value, expectedRevision: attachedRevision),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("binding_revision_conflict");
    }

    [Fact]
    public async Task ValidateAsync_WhenWorkspaceDefinitionIsArchived_RejectsAttach()
    {
        RuleDefinition definition = RuleDefinitionHandlerTestContext.VersionedDefinition();
        definition.Archive(definition.Revision, RuleSubjectReference.Human(RuleDefinitionHandlerTestContext.UserId), DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();
        RuleBinding binding = CreateBinding(
            RuleDefinitionHandlerTestContext.WorkspaceId,
            new Dictionary<string, RuleInputMapping>
            {
                ["value"] = RuleInputMapping.FromContext("record.value").Value,
                ["threshold"] = RuleInputMapping.FromLiteral(["10"]).Value,
            },
            definition.Key.Value);
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        bindings.GetByIdForWorkspaceAsync(binding.Id, binding.WorkspaceId, Arg.Any<CancellationToken>()).Returns(binding);
        IRuleDefinitionRepository definitions = Substitute.For<IRuleDefinitionRepository>();
        definitions.GetByKeyForWorkspaceAsync(definition.Key, binding.WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(definition);
        RuleBindingReferenceValidator sut = new(bindings, definitions);

        RuleBindingReferenceValidationResult result = await sut.ValidateAsync(Request(
            binding.WorkspaceId,
            binding.Id.Value,
            ContractRuleValueType.Decimal), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("binding_definition_unavailable");
    }

    [Fact]
    public async Task ValidateAsync_WhenExactWorkspaceDefinitionVersionIsMissing_RejectsAttach()
    {
        RuleDefinition definition = RuleDefinitionHandlerTestContext.VersionedDefinition();
        RuleBinding binding = CreateBinding(
            RuleDefinitionHandlerTestContext.WorkspaceId,
            new Dictionary<string, RuleInputMapping>
            {
                ["value"] = RuleInputMapping.FromContext("record.value").Value,
                ["threshold"] = RuleInputMapping.FromLiteral(["10"]).Value,
            },
            definition.Key.Value,
            version: 2);
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        bindings.GetByIdForWorkspaceAsync(binding.Id, binding.WorkspaceId, Arg.Any<CancellationToken>()).Returns(binding);
        IRuleDefinitionRepository definitions = Substitute.For<IRuleDefinitionRepository>();
        definitions.GetByKeyForWorkspaceAsync(definition.Key, binding.WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(definition);
        RuleBindingReferenceValidator sut = new(bindings, definitions);

        RuleBindingReferenceValidationResult result = await sut.ValidateAsync(Request(
            binding.WorkspaceId,
            binding.Id.Value,
            ContractRuleValueType.Decimal), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("binding_definition_unavailable");
    }

    [Fact]
    public async Task ValidateAsync_WhenBindingIsOutsideWorkspace_ReturnsNotFoundWithoutDefinitionLookup()
    {
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        IRuleDefinitionRepository definitions = Substitute.For<IRuleDefinitionRepository>();
        RuleBindingReferenceValidator sut = new(bindings, definitions);

        RuleBindingReferenceValidationResult result = await sut.ValidateAsync(
            Request(Guid.NewGuid(), Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("binding_not_found");
        await definitions.DidNotReceiveWithAnyArgs().GetByKeyForWorkspaceAsync(
            default,
            default,
            TestContext.Current.CancellationToken);
    }

    private static RuleBindingReferenceValidationRequest Request(
        Guid workspaceId,
        Guid bindingId,
        ContractRuleValueType type = ContractRuleValueType.Text,
        bool allowMultiple = false,
        int? expectedRevision = null) =>
        new(
            workspaceId,
            bindingId,
            "business-object-field",
            "customer.name",
            "field-validation",
            new Dictionary<string, RuleBindingContextValueSchema>
            {
                ["record.value"] = new(type, allowMultiple),
            },
            ["record.value"],
            expectedRevision);

    private static RuleBindingReferenceValidator CreateValidator(Guid workspaceId, RuleBinding binding)
    {
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        bindings.GetByIdForWorkspaceAsync(binding.Id, workspaceId, Arg.Any<CancellationToken>()).Returns(binding);
        return new RuleBindingReferenceValidator(bindings, Substitute.For<IRuleDefinitionRepository>());
    }

    private static Dictionary<string, RuleInputMapping> ContextMapping(string key) =>
        new() { ["value"] = RuleInputMapping.FromContext(key).Value };

    private static RuleBinding CreateBinding(
        Guid workspaceId,
        IReadOnlyDictionary<string, RuleInputMapping> mappings,
        string definitionKey = "field.required",
        int version = 1) =>
        RuleBinding.Create(
            workspaceId,
            RuleDefinitionKey.Create(definitionKey).Value,
            version,
            "business-object-field",
            "customer.name",
            "field-validation",
            mappings,
            priority: 0,
            enabled: true,
            DomainFailureBehavior.FailClosed,
            RuleSubjectReference.Human(Guid.NewGuid()),
            DateTime.UtcNow).Value;
}
