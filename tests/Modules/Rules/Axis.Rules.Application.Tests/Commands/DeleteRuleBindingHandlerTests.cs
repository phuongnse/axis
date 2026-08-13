using Axis.Identity.Contracts;
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
        DeleteRuleBindingHandler handler = new(context.CurrentUser, context.CurrentSubject, context.Authorization, bindings, context.UnitOfWork);

        Result result = await handler.Handle(
            new DeleteRuleBindingCommand(binding.Id.Value, binding.Revision),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bindings.Received(1).Remove(binding);
        await context.Authorization.Received(1).AuthorizeAsync(
            RuleDefinitionHandlerTestContext.WorkspaceId,
            context.CurrentSubject.Subject,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAuthorizationUnavailable_DoesNotRemoveBinding()
    {
        RuleBinding binding = RuleBindingHandlerTestData.Binding();
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        bindings.GetByIdForWorkspaceAsync(
                binding.Id,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(binding);
        context.Authorization.AuthorizeAsync(
                Arg.Any<Guid>(),
                Arg.Any<SubjectReference>(),
                Arg.Any<CancellationToken>())
            .Returns(WorkspaceProductBuilderDecision.Unavailable);
        DeleteRuleBindingHandler handler = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            bindings,
            context.UnitOfWork);

        Result result = await handler.Handle(
            new DeleteRuleBindingCommand(binding.Id.Value, binding.Revision),
            TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(ErrorCodes.Unavailable);
        bindings.DidNotReceive().Remove(Arg.Any<RuleBinding>());
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenExpectedRevisionIsStale_ReturnsConflictWithoutRemovingBinding()
    {
        RuleBinding binding = RuleBindingHandlerTestData.Binding();
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        bindings.GetByIdForWorkspaceAsync(
                binding.Id,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(binding);
        DeleteRuleBindingHandler handler = new(context.CurrentUser, context.CurrentSubject, context.Authorization, bindings, context.UnitOfWork);

        Result result = await handler.Handle(
            new DeleteRuleBindingCommand(binding.Id.Value, binding.Revision + 1),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        bindings.DidNotReceive().Remove(Arg.Any<RuleBinding>());
    }

    [Fact]
    public async Task Handle_WhenBindingWasInstalled_ReturnsConflictWithoutRemovingBinding()
    {
        RuleBinding binding = RuleBindingHandlerTestData.Binding();
        binding.AdvanceInstallationReceipt(
            Guid.NewGuid(),
            "field.required@1:business-object-field:invoice.field-1:record-save",
            new string('a', 64),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1).IsSuccess.Should().BeTrue();
        IRuleBindingRepository bindings = Substitute.For<IRuleBindingRepository>();
        RuleDefinitionHandlerTestContext context = new();
        bindings.GetByIdForWorkspaceAsync(
                binding.Id,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(binding);
        DeleteRuleBindingHandler handler = new(
            context.CurrentUser,
            context.CurrentSubject,
            context.Authorization,
            bindings,
            context.UnitOfWork);

        Result result = await handler.Handle(
            new DeleteRuleBindingCommand(binding.Id.Value, binding.Revision),
            TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        bindings.DidNotReceive().Remove(Arg.Any<RuleBinding>());
    }
}
