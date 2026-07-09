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
        result.Value.Fields[0].FieldType.Should().Be(ObjectFieldType.Text);
        result.Value.Fields[0].Variants.Should().BeEmpty();
        await _context.Repository.DidNotReceive().ObjectKeyExistsAsync(
            ObjectDefinitionHandlerTestContext.WorkspaceId,
            Arg.Any<ObjectDefinitionKey>(),
            definition.Id,
            Arg.Any<CancellationToken>());
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveUnpublished_WhenFieldVariantsAreValid_ReturnsTypedFieldContract()
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
                Fields:
                [
                    ObjectDefinitionHandlerTestContext.FieldInput(
                        "credit_limit",
                        "Credit limit",
                        ObjectFieldType.Decimal,
                        [
                            new(
                                ObjectFieldVariantKind.NumericRange,
                                MinNumber: 0,
                                MaxNumber: 100000),
                        ]),
                ]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ObjectFieldDefinitionDto field = result.Value.Fields.Should().ContainSingle().Subject;
        field.FieldType.Should().Be(ObjectFieldType.Decimal);
        field.Variants.Should().ContainSingle();
        field.Variants[0].Kind.Should().Be(ObjectFieldVariantKind.NumericRange);
        field.Variants[0].MinNumber.Should().Be(0);
        field.Variants[0].MaxNumber.Should().Be(100000);
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveUnpublished_WhenVariantIsInvalid_ReturnsInvalidWithoutCommit()
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
                Fields:
                [
                    ObjectDefinitionHandlerTestContext.FieldInput(
                        "is_active",
                        "Is active",
                        ObjectFieldType.Boolean,
                        [new(ObjectFieldVariantKind.TextLength, MinLength: 1)]),
                ]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        result.ProblemCode.Should().Be(ObjectsProblemCodes.ObjectDefinitionInvalid);
        await _context.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
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
