using Axis.Rules.Application.Commands.SaveRuleDefinitionDraft;
using Axis.Rules.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Commands;

public sealed class SaveRuleDefinitionDraftHandlerTests
{
    private readonly RuleDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task SaveDraft_WhenDatabaseConcurrencyWins_ReturnsConflict()
    {
        Axis.Rules.Domain.RuleDefinition definition = RuleDefinitionHandlerTestContext.DraftDefinition();
        _context.Repository.GetByKeyForWorkspaceAsync(
                definition.Key,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        _context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new ConcurrencyException());
        SaveRuleDefinitionDraftHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Repository,
            _context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new SaveRuleDefinitionDraftCommand(
                definition.Key.Value,
                definition.Revision,
                definition.Name,
                definition.Description,
                RuleDefinitionHandlerTestContext.DraftInputsDto(),
                RuleDefinitionHandlerTestContext.ConditionDto()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ProblemCode.Should().Be(RulesProblemCodes.DefinitionConflict);
    }
}
