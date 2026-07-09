using Axis.Rules.Application;
using Axis.Rules.Contracts;
using FluentAssertions;

namespace Axis.Rules.Application.Tests;

public sealed class SystemFieldRuleDefinitionProviderTests
{
    [Fact]
    public void ListFieldRuleDefinitions_WhenCalled_ReturnsDeterministicContractDtos()
    {
        SystemFieldRuleDefinitionProvider sut = new();

        IReadOnlyList<FieldRuleDefinitionDto> definitions = sut.ListFieldRuleDefinitions();

        definitions.Select(definition => definition.DefinitionKey)
            .Should().BeInAscendingOrder(StringComparer.Ordinal);
        definitions.Should().Contain(definition =>
            definition.DefinitionKey == FieldRuleDefinitionKeys.TextPattern &&
            definition.Parameters.Any(parameter => parameter.Key == "pattern" && parameter.IsRequired));
    }
}
