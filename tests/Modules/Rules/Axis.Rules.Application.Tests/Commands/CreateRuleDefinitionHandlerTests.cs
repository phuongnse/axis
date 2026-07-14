using Axis.Rules.Application.Commands.CreateRuleDefinition;
using Axis.Rules.Contracts;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Commands;

public sealed class CreateRuleDefinitionHandlerTests
{
    private readonly RuleDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task Create_WhenKeyIsAvailable_AddsDraft()
    {
        _context.Repository.KeyExistsAsync(
                Arg.Any<Axis.Rules.Domain.RuleDefinitionKey>(),
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(false);
        CreateRuleDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.ContextRegistry,
            _context.Repository,
            _context.UnitOfWork);

        Shared.Domain.Primitives.Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new CreateRuleDefinitionCommand(
                "Credit threshold",
                "Flags high credit values.",
                RuleScope.Field,
                RuleDefinitionHandlerTestContext.Schema.ContextKey,
                ContextSchemaVersion: 1,
                RuleOutcomeKind.Validation),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DefinitionKey.Should().Be("credit_threshold");
        await _context.Repository.Received(1).AddAsync(
            Arg.Any<Axis.Rules.Domain.RuleDefinition>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WhenScopeDoesNotMatchContext_RejectsDraft()
    {
        _context.Repository.KeyExistsAsync(
                Arg.Any<Axis.Rules.Domain.RuleDefinitionKey>(),
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(false);
        CreateRuleDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.ContextRegistry,
            _context.Repository,
            _context.UnitOfWork);

        Shared.Domain.Primitives.Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new CreateRuleDefinitionCommand(
                "Credit threshold",
                "Flags high credit values.",
                RuleScope.Record,
                RuleDefinitionHandlerTestContext.Schema.ContextKey,
                ContextSchemaVersion: 1,
                RuleOutcomeKind.Validation),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _context.Repository.DidNotReceive().AddAsync(
            Arg.Any<Axis.Rules.Domain.RuleDefinition>(),
            Arg.Any<CancellationToken>());
    }
}
