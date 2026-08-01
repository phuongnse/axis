using Axis.Rules.Application.Commands.CreateRuleBinding;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Commands;

public sealed class CreateRuleBindingHandlerTests
{
    [Fact]
    public async Task Handle_WhenRequestIsValid_PersistsBindingForPublishedVersion()
    {
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        RuleBinding? persisted = null;
        bindings.AddAsync(
                Arg.Do<RuleBinding>(binding => persisted = binding),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        CreateRuleBindingHandler handler = new(
            context.CurrentUser,
            context.Repository,
            bindings,
            context.UnitOfWork);

        Result<RuleBindingDto> result = await handler.Handle(
            new CreateRuleBindingCommand(RuleBindingHandlerTestData.Request("field-1")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await bindings.Received(1).AddAsync(Arg.Any<RuleBinding>(), Arg.Any<CancellationToken>());
        persisted.Should().NotBeNull();
        persisted!.DefinitionVersion.Should().Be(1);
        persisted.TargetId.Should().Be("field-1");
    }
}
