using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Rules.Domain.Tests;

public sealed class RuleBindingTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly RuleSubjectReference Actor = RuleSubjectReference.Human(UserId);
    private static readonly DateTime Now = new(2026, 7, 31, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WhenSameVersionHasDifferentTargets_CreatesIndependentBindings()
    {
        RuleDefinitionKey key = RuleDefinitionKey.Create("field.required").Value;
        RuleInputMapping mapping = RuleInputMapping.FromContext("record.value").Value;

        RuleBinding first = RuleBinding.Create(
            WorkspaceId, key, 1, "invoice-field", "field-1", "record.validate",
            new Dictionary<string, RuleInputMapping> { ["value"] = mapping },
            priority: 1, enabled: true, RuleBindingFailureBehavior.FailClosed, Actor, Now).Value;
        RuleBinding second = RuleBinding.Create(
            WorkspaceId, key, 1, "invoice-field", "field-2", "record.validate",
            new Dictionary<string, RuleInputMapping> { ["value"] = mapping },
            priority: 2, enabled: true, RuleBindingFailureBehavior.FailClosed, Actor, Now).Value;

        first.Id.Should().NotBe(second.Id);
        first.DefinitionKey.Should().Be(second.DefinitionKey);
        first.TargetId.Should().NotBe(second.TargetId);
    }

    [Fact]
    public void Update_WhenRevisionIsStale_ReturnsConflictWithoutChangingMapping()
    {
        RuleDefinitionKey key = RuleDefinitionKey.Create("field.required").Value;
        RuleBinding binding = RuleBinding.Create(
            WorkspaceId, key, 1, "invoice-field", "field-1", "record.validate",
            new Dictionary<string, RuleInputMapping>
            {
                ["value"] = RuleInputMapping.FromContext("record.value").Value,
            },
            priority: 0, enabled: true, RuleBindingFailureBehavior.FailClosed, Actor, Now).Value;

        Result result = binding.Update(
            expectedRevision: 99,
            key,
            1,
            "invoice-field",
            "field-2",
            "record.validate",
            new Dictionary<string, RuleInputMapping>
            {
                ["value"] = RuleInputMapping.FromContext("record.other").Value,
            },
            priority: 0,
            enabled: true,
            RuleBindingFailureBehavior.FailClosed,
            Actor,
            Now.AddMinutes(1));

        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        binding.TargetId.Should().Be("field-1");
        binding.InputMappings["value"].ContextKey.Should().Be("record.value");
    }

    [Fact]
    public void InputMapping_WhenPayloadExceedsDomainBounds_ReturnsFailure()
    {
        RuleInputMapping.FromContext($"record.{new string('a', 114)}").IsFailure.Should().BeTrue();
        RuleInputMapping.FromLiteral(Enumerable.Repeat("value", 1001).ToArray()).IsFailure.Should().BeTrue();
        RuleInputMapping.FromLiteral([new string('a', 4001)]).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void InstallationReceipt_WhenEpochAdvances_PreservesImmutableProvenance()
    {
        RuleBinding binding = CreateBinding();
        Guid solutionVersionId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        Guid operationId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        Guid stepId = Guid.Parse("55555555-5555-4555-8555-555555555555");

        binding.AdvanceInstallationReceipt(
            solutionVersionId,
            "field.required@1:business-object-field:invoice.amount:record-save",
            new string('a', 64),
            operationId,
            stepId,
            1).IsSuccess.Should().BeTrue();
        binding.AdvanceInstallationReceipt(
            solutionVersionId,
            "field.required@1:business-object-field:invoice.amount:record-save",
            new string('a', 64),
            operationId,
            stepId,
            2).IsSuccess.Should().BeTrue();

        binding.InstalledLeaseEpoch.Should().Be(2);
        binding.InstalledSolutionVersionId.Should().Be(solutionVersionId);
        binding.Update(
            binding.Revision,
            binding.DefinitionKey,
            binding.DefinitionVersion,
            binding.TargetType,
            binding.TargetId,
            binding.UseCaseOrTrigger,
            binding.InputMappings,
            binding.Priority,
            binding.Enabled,
            binding.FailureBehavior,
            Actor,
            Now.AddMinutes(1)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void InstallationReceipt_WhenEpochIsStale_ReturnsConflict()
    {
        RuleBinding binding = CreateBinding();
        Guid solutionVersionId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        Guid operationId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        Guid stepId = Guid.Parse("55555555-5555-4555-8555-555555555555");

        binding.AdvanceInstallationReceipt(
            solutionVersionId,
            "field.required@1:business-object-field:invoice.amount:record-save",
            new string('a', 64),
            operationId,
            stepId,
            2).IsSuccess.Should().BeTrue();
        Result stale = binding.AdvanceInstallationReceipt(
            solutionVersionId,
            "field.required@1:business-object-field:invoice.amount:record-save",
            new string('a', 64),
            operationId,
            stepId,
            1);

        stale.ErrorCode.Should().Be(ErrorCodes.Conflict);
        binding.InstalledLeaseEpoch.Should().Be(2);
    }

    private static RuleBinding CreateBinding() =>
        RuleBinding.Create(
            WorkspaceId,
            RuleDefinitionKey.Create("field.required").Value,
            1,
            "business-object-field",
            "invoice.amount",
            "record-save",
            new Dictionary<string, RuleInputMapping>
            {
                ["value"] = RuleInputMapping.FromContext("record.value").Value,
            },
            0,
            true,
            RuleBindingFailureBehavior.FailClosed,
            Actor,
            Now).Value;
}
