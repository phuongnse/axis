using Axis.Rules.Application.Commands.ArchiveRuleDefinition;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Commands;

public sealed class ArchiveRuleDefinitionHandlerTests
{
    private readonly RuleDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task Archive_WhenDefinitionIsPublished_ReturnsArchived()
    {
        Axis.Rules.Domain.RuleDefinition definition = RuleDefinitionHandlerTestContext.PublishedDefinition();
        _context.Repository.GetByKeyForWorkspaceAsync(
                definition.Key,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        ArchiveRuleDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new ArchiveRuleDefinitionCommand(definition.Key.Value, definition.Revision),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RuleLifecycleStatus.Archived);
    }

    [Fact]
    public async Task Archive_WhenDefinitionIsDraft_ReturnsInvalidWithoutPersistence()
    {
        Axis.Rules.Domain.RuleDefinition definition = RuleDefinitionHandlerTestContext.DraftDefinition();
        _context.Repository.GetByKeyForWorkspaceAsync(
                definition.Key,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        ArchiveRuleDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new ArchiveRuleDefinitionCommand(definition.Key.Value, definition.Revision),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        result.ProblemCode.Should().Be(RulesProblemCodes.DefinitionInvalid);
        await _context.UnitOfWork.DidNotReceive()
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Archive_WhenDefinitionIsSystem_ReturnsNotFoundWithoutPersistence()
    {
        ArchiveRuleDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new ArchiveRuleDefinitionCommand(RuleDefinitionKeys.Required, ExpectedRevision: 1),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ProblemCode.Should().Be(RulesProblemCodes.DefinitionNotFound);
        await _context.UnitOfWork.DidNotReceive()
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Archive_WhenDefinitionBelongsToAnotherWorkspace_ReturnsNotFoundWithoutPersistence()
    {
        Guid otherWorkspaceId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        _context.CurrentUser.workspaceId.Returns(otherWorkspaceId);
        Axis.Rules.Domain.RuleDefinition definition = RuleDefinitionHandlerTestContext.PublishedDefinition();
        ArchiveRuleDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new ArchiveRuleDefinitionCommand(definition.Key.Value, definition.Revision),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ProblemCode.Should().Be(RulesProblemCodes.DefinitionNotFound);
        await _context.Repository.Received(1).GetByKeyForWorkspaceAsync(
            definition.Key,
            otherWorkspaceId,
            Arg.Any<CancellationToken>());
        await _context.UnitOfWork.DidNotReceive()
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
