using Axis.Objects.Application;
using Axis.Objects.Application.Commands.SaveUnpublishedObjectDefinition;
using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Objects.Application.Tests.Commands;

public sealed class SaveUnpublishedObjectDefinitionHandlerTests
{
    private readonly ObjectDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task SaveUnpublished_WhenExpectedRevisionMatches_UpdatesFieldsAndReturnsNextRevision()
    {
        ObjectDefinition definition = ObjectDefinitionHandlerTestContext.UnpublishedWithOneSave();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                ObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        SaveUnpublishedObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new SaveUnpublishedObjectDefinitionCommand(
                definition.Id.Value,
                ExpectedRevision: 2,
                Name: "Customer Account",
                Fields: [ObjectDefinitionHandlerTestContext.FieldInput("credit_limit", "Credit limit")]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Customer Account");
        result.Value.ObjectKey.Should().Be("customer");
        result.Value.Revision.Should().Be(3);
        result.Value.Fields.Should().ContainSingle(field => field.FieldKey == "credit_limit");
        result.Value.Fields[0].Label.Should().Be("Credit limit");
        await _context.Repository.DidNotReceive().ObjectKeyExistsAsync(
            ObjectDefinitionHandlerTestContext.WorkspaceId,
            Arg.Any<ObjectDefinitionKey>(),
            definition.Id,
            Arg.Any<CancellationToken>());
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveUnpublished_WhenExpectedRevisionIsStale_ReturnsConflictWithoutCommit()
    {
        ObjectDefinition definition = ObjectDefinitionHandlerTestContext.UnpublishedWithOneSave();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                ObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        SaveUnpublishedObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new SaveUnpublishedObjectDefinitionCommand(
                definition.Id.Value,
                ExpectedRevision: 1,
                Name: "Customer",
                Fields: [ObjectDefinitionHandlerTestContext.FieldInput("name", "Name")]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ProblemCode.Should().Be(ObjectsProblemCodes.ObjectDefinitionConflict);
        await _context.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
