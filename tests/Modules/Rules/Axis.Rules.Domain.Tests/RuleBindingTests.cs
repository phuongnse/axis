using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Rules.Domain.Tests;

public sealed class RuleBindingTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTime Now = new(2026, 7, 31, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WhenSameVersionHasDifferentTargets_CreatesIndependentBindings()
    {
        RuleDefinitionKey key = RuleDefinitionKey.Create("field.required").Value;
        RuleInputMapping mapping = RuleInputMapping.FromContext("record.value").Value;

        RuleBinding first = RuleBinding.Create(
            WorkspaceId, key, 1, "invoice-field", "field-1", "record.validate",
            new Dictionary<string, RuleInputMapping> { ["value"] = mapping },
            priority: 1, enabled: true, RuleBindingFailureBehavior.FailClosed, UserId, Now).Value;
        RuleBinding second = RuleBinding.Create(
            WorkspaceId, key, 1, "invoice-field", "field-2", "record.validate",
            new Dictionary<string, RuleInputMapping> { ["value"] = mapping },
            priority: 2, enabled: true, RuleBindingFailureBehavior.FailClosed, UserId, Now).Value;

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
            priority: 0, enabled: true, RuleBindingFailureBehavior.FailClosed, UserId, Now).Value;

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
            UserId,
            Now.AddMinutes(1));

        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        binding.TargetId.Should().Be("field-1");
        binding.InputMappings["value"].ContextKey.Should().Be("record.value");
    }
}
