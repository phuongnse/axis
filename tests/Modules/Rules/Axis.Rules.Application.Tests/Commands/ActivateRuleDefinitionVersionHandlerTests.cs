using Axis.Rules.Application.Commands.ActivateRuleDefinitionVersion;
using Axis.Rules.Contracts;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Commands;

public sealed class ActivateRuleDefinitionVersionHandlerTests
{
    [Fact]
    public async Task Handle_WhenVersionExists_ActivatesThatExactVersion()
    {
        RuleDefinitionHandlerTestContext context = new();
        Axis.Rules.Domain.RuleDefinition definition = RuleDefinitionHandlerTestContext.VersionedDefinition();
        context.Repository.GetByKeyForWorkspaceAsync(definition.Key, RuleDefinitionHandlerTestContext.WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(definition);
        ActivateRuleDefinitionVersionHandler sut = new(context.CurrentUser, context.Repository, context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(new ActivateRuleDefinitionVersionCommand(definition.Key.Value, 1, definition.Revision), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RuleLifecycleStatus.Active);
        result.Value.ActiveVersion.Should().Be(1);
    }
}
