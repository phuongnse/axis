using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Commands.SaveUnpublishedBusinessObjectDefinition;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Rules.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests.Commands;

public sealed class SaveUnpublishedBusinessObjectDefinitionHandlerTests
{
    private readonly BusinessObjectDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task SaveUnpublished_WhenExpectedRevisionMatches_UpdatesFieldsAndReturnsNextRevision()
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.UnpublishedWithOneSave();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        SaveUnpublishedBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork,
            _context.InputPlanner);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new SaveUnpublishedBusinessObjectDefinitionCommand(
                definition.Id.Value,
                ExpectedRevision: 2,
                Name: "Customer Account",
                Fields: [BusinessObjectDefinitionHandlerTestContext.FieldInput("credit_limit", "Credit limit")]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Customer Account");
        result.Value.ObjectKey.Should().Be("customer");
        result.Value.Revision.Should().Be(3);
        result.Value.Fields.Should().ContainSingle(field => field.FieldKey == "credit_limit");
        result.Value.Fields[0].Label.Should().Be("Credit limit");
        result.Value.Fields[0].FieldType.Should().Be(BusinessObjectFieldType.Text);
        result.Value.Fields[0].Rules.Should().BeEmpty();
        await _context.Repository.DidNotReceive().ObjectKeyExistsAsync(
            BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
            Arg.Any<BusinessObjectDefinitionKey>(),
            definition.Id,
            Arg.Any<CancellationToken>());
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveUnpublished_WhenFieldRulesAreValid_ReturnsTypedFieldContract()
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.UnpublishedWithOneSave();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        SaveUnpublishedBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork,
            _context.InputPlanner);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new SaveUnpublishedBusinessObjectDefinitionCommand(
                definition.Id.Value,
                ExpectedRevision: 2,
                Name: "Customer Account",
                Fields:
                [
                    BusinessObjectDefinitionHandlerTestContext.FieldInput(
                        "credit_limit",
                        "Credit limit",
                        BusinessObjectFieldType.Decimal,
                        [
                            new(
                                RuleDefinitionKeys.NumericRange,
                                DefinitionVersion: 1,
                                Params(("min", ["0"]), ("max", ["100000"]))),
                        ]),
                ]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        BusinessObjectFieldDefinitionDto field = result.Value.Fields.Should().ContainSingle().Subject;
        field.FieldType.Should().Be(BusinessObjectFieldType.Decimal);
        field.Rules.Should().ContainSingle();
        field.Rules[0].DefinitionKey.Should().Be(RuleDefinitionKeys.NumericRange);
        field.Rules[0].DefinitionVersion.Should().Be(1);
        field.Rules[0].Parameters["min"].Should().Equal("0");
        field.Rules[0].Parameters["max"].Should().Equal("100000");
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveUnpublished_WhenFieldRuleIsInvalid_ReturnsInvalidWithoutCommit()
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.UnpublishedWithOneSave();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        SaveUnpublishedBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork,
            _context.InputPlanner);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new SaveUnpublishedBusinessObjectDefinitionCommand(
                definition.Id.Value,
                ExpectedRevision: 2,
                Name: "Customer Account",
                Fields:
                [
                    BusinessObjectDefinitionHandlerTestContext.FieldInput(
                        "is_active",
                        "Is active",
                        BusinessObjectFieldType.Boolean,
                        [new(RuleDefinitionKeys.TextLength, DefinitionVersion: 1, Params(("min", ["1"])))]),
                ]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        result.ProblemCode.Should().Be(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);
        await _context.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task SaveUnpublished_WhenExpectedRevisionIsStale_ReturnsConflictWithoutCommit()
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.UnpublishedWithOneSave();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        SaveUnpublishedBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork,
            _context.InputPlanner);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new SaveUnpublishedBusinessObjectDefinitionCommand(
                definition.Id.Value,
                ExpectedRevision: 1,
                Name: "Customer",
                Fields: [BusinessObjectDefinitionHandlerTestContext.FieldInput("name", "Name")]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ProblemCode.Should().Be(BusinessObjectsProblemCodes.BusinessObjectDefinitionConflict);
        await _context.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Params(
        params (string Key, string[] Values)[] parameters) =>
        parameters.ToDictionary(
            parameter => parameter.Key,
            parameter => (IReadOnlyList<string>)parameter.Values,
            StringComparer.Ordinal);
}
