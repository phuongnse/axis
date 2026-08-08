using Axis.Authorization.Contracts;
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
            context.CurrentSubject,
            context.Authorization,
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

    [Fact]
    public async Task Handle_WhenAuthorizationUnavailable_UsesRequestedDefinitionKeyWithoutMutation()
    {
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        ProductAuthorizationRequest? observed = null;
        context.Authorization.AuthorizeAsync(
                Arg.Do<ProductAuthorizationRequest>(request => observed = request),
                Arg.Any<CancellationToken>())
            .Returns(ProductAuthorizationDecision.Unavailable);
        CreateRuleBindingHandler handler = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository,
            bindings,
            context.UnitOfWork);

        Result<RuleBindingDto> result = await handler.Handle(
            new CreateRuleBindingCommand(RuleBindingHandlerTestData.Request("field-1")),
            TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(ErrorCodes.Unavailable);
        observed!.ResourceKey.Should().Be(RuleDefinitionKeys.Required);
        await context.Repository.DidNotReceive().GetByKeyForWorkspaceAsync(
            Arg.Any<RuleDefinitionKey>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await bindings.DidNotReceive().AddAsync(
            Arg.Any<RuleBinding>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenInstalledIdentityExists_ReturnsConflictWithoutAddingBinding()
    {
        RuleBinding installed = RuleBindingHandlerTestData.Binding();
        installed.AdvanceInstallationReceipt(
            Guid.NewGuid(),
            "field.required@1:business-object-field:invoice.field-1:record-save",
            new string('a', 64),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1).IsSuccess.Should().BeTrue();
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        bindings.GetByIdentityForWorkspaceAsync(
                Arg.Any<Guid>(),
                Arg.Any<RuleDefinitionKey>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(installed);
        CreateRuleBindingHandler handler = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository,
            bindings,
            context.UnitOfWork);

        Result<RuleBindingDto> result = await handler.Handle(
            new CreateRuleBindingCommand(RuleBindingHandlerTestData.Request("field-1")),
            TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        await bindings.DidNotReceive().AddAsync(
            Arg.Any<RuleBinding>(),
            Arg.Any<CancellationToken>());
    }
}
