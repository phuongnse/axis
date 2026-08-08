using Axis.Authorization.Contracts;
using Axis.Rules.Application.Commands.UpdateRuleBinding;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Commands;

public sealed class UpdateRuleBindingHandlerTests
{
    [Fact]
    public async Task Handle_WhenRequestIsValid_UpdatesBindingWithoutChangingDefinition()
    {
        RuleDefinition definition = BuiltInRuleCatalog.Find("field.required", 1)!;
        int definitionRevision = definition.Revision;
        RuleBinding binding = RuleBindingHandlerTestData.Binding();
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        bindings.GetByIdForWorkspaceAsync(
                binding.Id,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(binding);
        UpdateRuleBindingHandler handler = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository,
            bindings,
            context.UnitOfWork);

        Result<RuleBindingDto> result = await handler.Handle(
            new UpdateRuleBindingCommand(
                binding.Id.Value,
                new UpdateRuleBindingRequest(
                    binding.Revision,
                    "field.required",
                    1,
                    "invoice-field",
                    "field-2",
                    "record.validate",
                    RuleBindingHandlerTestData.Mappings(),
                    Priority: 10,
                    Enabled: false)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        binding.TargetId.Should().Be("field-2");
        binding.Enabled.Should().BeFalse();
        binding.Revision.Should().Be(2);
        definition.Revision.Should().Be(definitionRevision);
    }

    [Theory]
    [InlineData(ProductAuthorizationDecisionStatus.Denied, ErrorCodes.Forbidden)]
    [InlineData(ProductAuthorizationDecisionStatus.Unavailable, ErrorCodes.Unavailable)]
    public async Task Handle_WhenRetargetAuthorizationFails_AuthorizesOldThenNewAndDoesNotMutate(
        ProductAuthorizationDecisionStatus status,
        string expectedErrorCode)
    {
        RuleBinding binding = RuleBindingHandlerTestData.Binding();
        int originalRevision = binding.Revision;
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        List<string?> observedKeys = [];
        bindings.GetByIdForWorkspaceAsync(
                binding.Id,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(binding);
        context.Authorization.AuthorizeAsync(
                Arg.Do<ProductAuthorizationRequest>(request => observedKeys.Add(request.ResourceKey)),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ProductAuthorizationRequest>().ResourceKey == binding.DefinitionKey.Value
                ? new ProductAuthorizationDecision(true, ProductActionScope.None)
                : status == ProductAuthorizationDecisionStatus.Unavailable
                    ? ProductAuthorizationDecision.Unavailable
                    : ProductAuthorizationDecision.Denied);
        UpdateRuleBindingHandler handler = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            context.Repository,
            bindings,
            context.UnitOfWork);

        Result<RuleBindingDto> result = await handler.Handle(
            new UpdateRuleBindingCommand(
                binding.Id.Value,
                new UpdateRuleBindingRequest(
                    binding.Revision,
                    RuleDefinitionKeys.TextLength,
                    1,
                    binding.TargetType,
                    binding.TargetId,
                    binding.UseCaseOrTrigger,
                    RuleBindingHandlerTestData.Mappings())),
            TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(expectedErrorCode);
        observedKeys.Should().Equal(RuleDefinitionKeys.Required, RuleDefinitionKeys.TextLength);
        binding.DefinitionKey.Value.Should().Be(RuleDefinitionKeys.Required);
        binding.Revision.Should().Be(originalRevision);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
