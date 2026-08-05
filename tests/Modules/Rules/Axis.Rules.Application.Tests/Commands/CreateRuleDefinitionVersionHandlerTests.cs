using Axis.Rules.Application.Commands.CreateRuleDefinitionVersion;
using Axis.Rules.Contracts;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Commands;

public sealed class CreateRuleDefinitionVersionHandlerTests
{
    [Fact]
    public async Task Handle_WhenDraftIsConfigured_CreatesInactiveVersionWithoutChangingDraft()
    {
        RuleDefinitionHandlerTestContext context = new();
        Axis.Rules.Domain.RuleDefinition definition = RuleDefinitionHandlerTestContext.ConfiguredDraft();
        context.Repository.GetByKeyForWorkspaceAsync(definition.Key, RuleDefinitionHandlerTestContext.WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(definition);
        CreateRuleDefinitionVersionHandler sut = new(context.CurrentUser, context.Repository, context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(new CreateRuleDefinitionVersionCommand(definition.Key.Value, definition.Revision), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RuleLifecycleStatus.Inactive);
        result.Value.LatestVersion.Should().Be(1);
        result.Value.ActiveVersion.Should().BeNull();
        result.Value.Condition.Should().NotBeNull();
    }
}
