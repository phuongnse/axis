using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Commands.PublishBusinessObjectDefinition;
using Axis.BusinessObjects.Domain.Aggregates;
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
            _context.Repository,
            _context.UnitOfWork);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new PublishBusinessObjectDefinitionCommand(definition.Id.Value, ExpectedRevision: 2),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(BusinessObjectDefinitionStatus.Published);
        result.Value.LatestPublishedVersionNumber.Should().Be(1);
        result.Value.LatestPublishedVersion.Should().NotBeNull();
        result.Value.LatestPublishedVersion!.PublishedByUserId
            .Should().Be(BusinessObjectDefinitionHandlerTestContext.UserId);
        result.Value.LatestPublishedVersion.Fields.Should()
            .ContainSingle(field => field.FieldKey == "name");
        result.Value.LatestPublishedVersion.Fields[0].FieldType.Should().Be(BusinessObjectFieldType.Text);
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_WhenUserScopeIsMissing_ReturnsForbiddenWithoutCommit()
    {
        _context.CurrentUser.UserId = null;
        PublishBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new PublishBusinessObjectDefinitionCommand(Guid.NewGuid(), ExpectedRevision: 1),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ProblemCode.Should().Be(BusinessObjectsProblemCodes.UserScopeRequired);
        await _context.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
