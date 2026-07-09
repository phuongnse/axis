using Axis.Objects.Application;
using Axis.Objects.Application.Commands.PublishObjectDefinition;
using Axis.Objects.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Objects.Application.Tests.Commands;

public sealed class PublishObjectDefinitionHandlerTests
{
    private readonly ObjectDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task Publish_WhenUnpublishedDefinitionIsValid_PersistsPublishedVersionAuditMetadata()
    {
        ObjectDefinition definition = ObjectDefinitionHandlerTestContext.UnpublishedWithOneSave();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                ObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        PublishObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new PublishObjectDefinitionCommand(definition.Id.Value, ExpectedRevision: 2),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ObjectDefinitionStatus.Published);
        result.Value.LatestPublishedVersionNumber.Should().Be(1);
        result.Value.LatestPublishedVersion.Should().NotBeNull();
        result.Value.LatestPublishedVersion!.PublishedByUserId
            .Should().Be(ObjectDefinitionHandlerTestContext.UserId);
        result.Value.LatestPublishedVersion.Fields.Should()
            .ContainSingle(field => field.FieldKey == "name");
        result.Value.LatestPublishedVersion.Fields[0].FieldType.Should().Be(ObjectFieldType.Text);
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_WhenUserScopeIsMissing_ReturnsForbiddenWithoutCommit()
    {
        _context.CurrentUser.UserId = null;
        PublishObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new PublishObjectDefinitionCommand(Guid.NewGuid(), ExpectedRevision: 1),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ProblemCode.Should().Be(ObjectsProblemCodes.UserScopeRequired);
        await _context.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
