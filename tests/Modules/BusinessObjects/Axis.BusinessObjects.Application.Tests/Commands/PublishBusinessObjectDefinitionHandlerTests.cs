using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Commands.PublishBusinessObjectDefinition;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Identity.Contracts;
using Axis.Rules.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests.Commands;

public sealed class PublishBusinessObjectDefinitionHandlerTests
{
    private readonly BusinessObjectDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task Publish_WhenUnpublishedDefinitionIsValid_PersistsPublishedVersionAuditMetadata()
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.UnpublishedWithOneSave();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        PublishBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Repository,
            _context.UnitOfWork,
            _context.BindingValidator);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new PublishBusinessObjectDefinitionCommand(definition.Id.Value, ExpectedRevision: 2),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(BusinessObjectDefinitionStatus.Published);
        result.Value.LatestPublishedVersionNumber.Should().Be(1);
        result.Value.LatestPublishedVersion.Should().NotBeNull();
        result.Value.LatestPublishedVersion!.PublishedBySubject.Kind.Should().Be(SubjectKind.Human);
        result.Value.LatestPublishedVersion.PublishedBySubject.SubjectId
            .Should().Be(BusinessObjectDefinitionHandlerTestContext.UserId);
        result.Value.LatestPublishedVersion.Fields.Should()
            .ContainSingle(field => field.FieldKey == "name");
        result.Value.LatestPublishedVersion.Fields[0].FieldType.Should().Be(BusinessObjectFieldType.Text);
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_WhenUserScopeIsMissing_ReturnsForbiddenWithoutCommit()
    {
        _context.CurrentSubject.Subject = default;
        PublishBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Repository,
            _context.UnitOfWork,
            _context.BindingValidator);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new PublishBusinessObjectDefinitionCommand(Guid.NewGuid(), ExpectedRevision: 1),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ProblemCode.Should().Be(BusinessObjectsProblemCodes.UserScopeRequired);
        await _context.UnitOfWork.DidNotReceiveWithAnyArgs()
            .SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Publish_WhenBindingRevisionChanged_ReturnsConflictWithoutCommit()
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.CreateUnpublished("Customer", "customer");
        definition.SaveUnpublished(
            "Customer",
            [new BusinessObjectFieldDefinitionSpec(
                "name",
                "Name",
                0,
                BusinessObjectFieldType.Text,
                [new BusinessObjectFieldRuleSpec(TestBindingIds.TextLength, null, 1)])],
            expectedRevision: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        _context.BindingValidator.ValidateAsync(
                Arg.Any<RuleBindingReferenceValidationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(RuleBindingReferenceValidationResult.Invalid(
                "binding_revision_conflict",
                "Rule binding has changed.")));
        PublishBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Repository,
            _context.UnitOfWork,
            _context.BindingValidator);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new PublishBusinessObjectDefinitionCommand(definition.Id.Value, ExpectedRevision: 2),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ProblemCode.Should().Be(BusinessObjectsProblemCodes.BusinessObjectDefinitionConflict);
        await _context.UnitOfWork.DidNotReceiveWithAnyArgs()
            .SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Publish_WhenBindingContextIsIncompatible_ReturnsInvalidWithoutCommit()
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.CreateUnpublished("Customer", "customer");
        definition.SaveUnpublished(
            "Customer",
            [new BusinessObjectFieldDefinitionSpec(
                "name",
                "Name",
                0,
                BusinessObjectFieldType.Text,
                [new BusinessObjectFieldRuleSpec(TestBindingIds.TextLength, null, 1)])],
            expectedRevision: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        _context.BindingValidator.ValidateAsync(
                Arg.Any<RuleBindingReferenceValidationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(RuleBindingReferenceValidationResult.Invalid(
                "binding_context_type_mismatch",
                "Rule binding consumer context type does not match the rule input.")));
        PublishBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Repository,
            _context.UnitOfWork,
            _context.BindingValidator);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new PublishBusinessObjectDefinitionCommand(definition.Id.Value, ExpectedRevision: 2),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        result.ProblemCode.Should().Be(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);
        definition.Status.Should().Be(BusinessObjectDefinitionStatus.Unpublished);
        await _context.UnitOfWork.DidNotReceiveWithAnyArgs()
            .SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
