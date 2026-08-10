using Axis.Rules.Application.Queries.ListRuleBindingUsage;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Queries;

public sealed class ListRuleBindingUsageHandlerTests
{
    [Fact]
    public async Task Handle_WhenDefinitionHasBindings_ReturnsUsageWithoutDefinitionMutation()
    {
        RuleDefinition definition = BuiltInRuleCatalog.Find("field.required", 1)!;
        int definitionRevision = definition.Revision;
        RuleBinding binding = RuleBindingHandlerTestData.Binding("field-2");
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        bindings.ListByDefinitionAsync(
                definition.Key,
                1,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns([binding]);
        ListRuleBindingUsageHandler handler = new(context.CurrentUser, context.CurrentSubject, context.Authorization, bindings);

        Result<IReadOnlyList<RuleBindingUsageDto>> result = await handler.Handle(
            new ListRuleBindingUsageQuery("field.required", 1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(item =>
            item.BindingId == binding.Id.Value && item.TargetId == "field-2");
        definition.Revision.Should().Be(definitionRevision);
    }
}
