using Axis.Rules.Application.Commands.DeactivateRuleDefinition;
using Axis.Rules.Contracts;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Commands;

public sealed class DeactivateRuleDefinitionHandlerTests
{
    [Fact]
    public async Task Handle_WhenVersionIsActive_DeactivatesWithoutChangingLatestVersion()
    {
        RuleDefinitionHandlerTestContext context = new();
        Axis.Rules.Domain.RuleDefinition definition = RuleDefinitionHandlerTestContext.ActiveDefinition();
        context.Repository.GetByKeyForWorkspaceAsync(definition.Key, RuleDefinitionHandlerTestContext.WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(definition);
        DeactivateRuleDefinitionHandler sut = new(context.CurrentUser, context.Repository, context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(new DeactivateRuleDefinitionCommand(definition.Key.Value, definition.Revision), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RuleLifecycleStatus.Inactive);
        result.Value.LatestVersion.Should().Be(1);
        result.Value.ActiveVersion.Should().BeNull();
    }
}
