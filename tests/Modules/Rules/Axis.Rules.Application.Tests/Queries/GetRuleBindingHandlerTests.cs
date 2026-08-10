using Axis.Authorization.Contracts;
using Axis.Rules.Application.Queries.GetRuleBinding;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Queries;

public sealed class GetRuleBindingHandlerTests
{
    [Fact]
    public async Task Handle_WhenBindingExists_ReturnsFullBindingForCurrentWorkspace()
    {
        RuleBinding binding = RuleBindingHandlerTestData.Binding();
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        bindings.GetByIdForWorkspaceAsync(
                binding.Id,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(binding);
        GetRuleBindingHandler handler = new(context.CurrentUser, context.CurrentSubject, context.Authorization, bindings);

        Result<RuleBindingDto> result = await handler.Handle(
            new GetRuleBindingQuery(binding.Id.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(binding.Id.Value);
        result.Value.WorkspaceId.Should().Be(RuleDefinitionHandlerTestContext.WorkspaceId);
        result.Value.DefinitionKey.Should().Be("field.required");
        result.Value.DefinitionVersion.Should().Be(1);
        result.Value.InputMappings["value"].LiteralValues.Should().Equal("Approved");
        result.Value.Revision.Should().Be(binding.Revision);
    }

    [Fact]
    public async Task Handle_WhenBindingIsOutsideCurrentWorkspace_ReturnsNotFound()
    {
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        GetRuleBindingHandler handler = new(context.CurrentUser, context.CurrentSubject, context.Authorization, bindings);

        Result<RuleBindingDto> result = await handler.Handle(
            new GetRuleBindingQuery(Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await context.Authorization.DidNotReceive().AuthorizeAsync(
            Arg.Any<ProductAuthorizationRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAuthorizationUnavailable_UsesPersistedDefinitionKey()
    {
        RuleBinding binding = RuleBindingHandlerTestData.Binding();
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        ProductAuthorizationRequest? observed = null;
        bindings.GetByIdForWorkspaceAsync(
                binding.Id,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(binding);
        context.Authorization.AuthorizeAsync(
                Arg.Do<ProductAuthorizationRequest>(request => observed = request),
                Arg.Any<CancellationToken>())
            .Returns(ProductAuthorizationDecision.Unavailable);
        GetRuleBindingHandler handler = new(context.CurrentUser, context.CurrentSubject, context.Authorization, bindings);

        Result<RuleBindingDto> result = await handler.Handle(
            new GetRuleBindingQuery(binding.Id.Value),
            TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(ErrorCodes.Unavailable);
        observed!.ResourceKey.Should().Be(binding.DefinitionKey.Value);
    }
}
