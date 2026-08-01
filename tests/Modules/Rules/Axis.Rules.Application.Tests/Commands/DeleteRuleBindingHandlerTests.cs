using Axis.Rules.Application.Commands.DeleteRuleBinding;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Commands;

public sealed class DeleteRuleBindingHandlerTests
{
    [Fact]
    public async Task Handle_WhenBindingExists_RemovesBinding()
    {
        RuleBinding binding = RuleBindingHandlerTestData.Binding();
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        bindings.GetByIdForWorkspaceAsync(
                binding.Id,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(binding);
        DeleteRuleBindingHandler handler = new(context.CurrentUser, bindings, context.UnitOfWork);

        Result result = await handler.Handle(
            new DeleteRuleBindingCommand(binding.Id.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bindings.Received(1).Remove(binding);
    }
}
