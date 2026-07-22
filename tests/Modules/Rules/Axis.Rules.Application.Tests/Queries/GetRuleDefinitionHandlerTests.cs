using Axis.Rules.Application.Queries.GetRuleDefinition;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using DomainExpressionLanguage = Axis.Rules.Domain.RuleExpressionLanguage;

namespace Axis.Rules.Application.Tests.Queries;

public sealed class GetRuleDefinitionHandlerTests
{
    private readonly RuleDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task Get_WhenDefinitionExists_ReturnsDetail()
    {
        Axis.Rules.Domain.RuleDefinition definition = RuleDefinitionHandlerTestContext.DraftDefinition();
        _context.Repository.GetByKeyForWorkspaceAsync(
                definition.Key,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        GetRuleDefinitionHandler sut = new(_context.CurrentUser, _context.Repository);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new GetRuleDefinitionQuery(definition.Key.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DefinitionKey.Should().Be(definition.Key.Value);
    }

    [Fact]
    public async Task Get_WhenSystemDefinitionExists_ReturnsExecutableReadOnlyDetail()
    {
        GetRuleDefinitionHandler sut = new(_context.CurrentUser, _context.Repository);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new GetRuleDefinitionQuery(RuleDefinitionKeys.Required),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Origin.Should().Be(RuleOrigin.System);
        result.Value.Status.Should().Be(RuleLifecycleStatus.Published);
        result.Value.ExpressionLanguageVersion.Should().Be(DomainExpressionLanguage.Version);
        result.Value.Applicability.Should().NotBeNull();
        result.Value.Condition.Should().NotBeNull();
        result.Value.Condition!.Left!.Kind.Should().Be(RuleOperandKind.Function);
        result.Value.Condition.Left.Function.Should().Be(RuleExpressionFunction.IsBlank);
        result.Value.Outcome.Should().NotBeNull();
        await _context.Repository.DidNotReceiveWithAnyArgs()
            .GetByKeyForWorkspaceAsync(default, default, default);
    }
}
