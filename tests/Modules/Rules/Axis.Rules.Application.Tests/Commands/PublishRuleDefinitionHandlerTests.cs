using Axis.Rules.Application.Commands.PublishRuleDefinition;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Commands;

public sealed class PublishRuleDefinitionHandlerTests
{
    private readonly RuleDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task Publish_WhenDraftIsConfigured_PublishesVersionOne()
    {
        Axis.Rules.Domain.RuleDefinition definition = RuleDefinitionHandlerTestContext.DraftDefinition(configured: true);
        _context.Repository.GetByKeyForWorkspaceAsync(
                definition.Key,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        PublishRuleDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.ContextRegistry,
            _context.Repository,
            _context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new PublishRuleDefinitionCommand(definition.Key.Value, definition.Revision),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RuleLifecycleStatus.Published);
        result.Value.LatestPublishedVersion.Should().Be(1);
    }
}
