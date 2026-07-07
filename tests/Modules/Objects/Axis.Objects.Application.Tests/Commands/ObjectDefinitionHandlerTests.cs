using Axis.Objects.Application;
using Axis.Objects.Application.Commands.CreateObjectDefinitionDraft;
using Axis.Objects.Application.Commands.PublishObjectDefinition;
using Axis.Objects.Application.Commands.SaveObjectDefinitionDraft;
using Axis.Objects.Application.Queries.GetObjectDefinition;
using Axis.Objects.Application.Queries.ListObjectDefinitions;
using Axis.Objects.Application.Repositories;
using Axis.Objects.Application.Services;
using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Objects.Application.Tests.Commands;

public sealed class ObjectDefinitionHandlerTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private readonly IObjectDefinitionRepository _repository = Substitute.For<IObjectDefinitionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeCurrentUser _currentUser = new()
    {
        UserId = UserId,
        workspaceId = WorkspaceId,
    };

    [Fact]
    public async Task CreateDraft_WhenWorkspaceScopedKeyIsAvailable_AddsDraftAndReturnsVersionOne()
    {
        _repository.ObjectKeyExistsAsync(
                WorkspaceId,
                Arg.Any<ObjectDefinitionKey>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(false);

        CreateObjectDefinitionDraftHandler sut = new(_currentUser, _repository, _unitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new CreateObjectDefinitionDraftCommand("Customer"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.WorkspaceId.Should().Be(WorkspaceId);
        result.Value.ObjectKey.Should().Be("customer");
        result.Value.DraftVersion.Should().Be(1);
        result.Value.Status.Should().Be(ObjectDefinitionStatus.Draft);
        await _repository.Received(1).AddAsync(Arg.Any<ObjectDefinition>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateDraft_WhenWorkspaceScopeIsMissing_ReturnsForbiddenWithoutMutation()
    {
        _currentUser.workspaceId = null;
        CreateObjectDefinitionDraftHandler sut = new(_currentUser, _repository, _unitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new CreateObjectDefinitionDraftCommand("Customer"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ProblemCode.Should().Be(ObjectsProblemCodes.WorkspaceScopeRequired);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task CreateDraft_WhenNameNeedsNormalization_DerivesServerOwnedObjectKey()
    {
        _repository.ObjectKeyExistsAsync(
                WorkspaceId,
                Arg.Is<ObjectDefinitionKey>(key => key.Value == "customer_account_2026"),
                null,
                Arg.Any<CancellationToken>())
            .Returns(false);
        CreateObjectDefinitionDraftHandler sut = new(_currentUser, _repository, _unitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new CreateObjectDefinitionDraftCommand("Customer Account 2026!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ObjectKey.Should().Be("customer_account_2026");
    }

    [Fact]
    public async Task CreateDraft_WhenObjectKeyAlreadyExists_ReturnsConflict()
    {
        _repository.ObjectKeyExistsAsync(
                WorkspaceId,
                Arg.Any<ObjectDefinitionKey>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(true);
        CreateObjectDefinitionDraftHandler sut = new(_currentUser, _repository, _unitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new CreateObjectDefinitionDraftCommand("Customer"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ProblemCode.Should().Be(ObjectsProblemCodes.ObjectKeyAlreadyExists);
    }

    [Fact]
    public async Task SaveDraft_WhenExpectedVersionMatches_UpdatesFieldsAndReturnsNextDraftVersion()
    {
        ObjectDefinition definition = DraftWithOneSave();
        _repository.GetByIdForWorkspaceAsync(definition.Id, WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(definition);
        SaveObjectDefinitionDraftHandler sut = new(_currentUser, _repository, _unitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new SaveObjectDefinitionDraftCommand(
                definition.Id.Value,
                ExpectedDraftVersion: 2,
                Name: "Customer Account",
                Fields: [FieldInput("credit_limit", "Credit limit")]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Customer Account");
        result.Value.ObjectKey.Should().Be("customer");
        result.Value.DraftVersion.Should().Be(3);
        result.Value.Fields.Should().ContainSingle(field => field.FieldKey == "credit_limit");
        result.Value.Fields[0].Label.Should().Be("Credit limit");
        await _repository.DidNotReceive().ObjectKeyExistsAsync(
            WorkspaceId,
            Arg.Any<ObjectDefinitionKey>(),
            definition.Id,
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveDraft_WhenExpectedVersionIsStale_ReturnsConflictWithoutCommit()
    {
        ObjectDefinition definition = DraftWithOneSave();
        _repository.GetByIdForWorkspaceAsync(definition.Id, WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(definition);
        SaveObjectDefinitionDraftHandler sut = new(_currentUser, _repository, _unitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new SaveObjectDefinitionDraftCommand(
                definition.Id.Value,
                ExpectedDraftVersion: 1,
                Name: "Customer",
                Fields: [FieldInput("name", "Name")]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ProblemCode.Should().Be(ObjectsProblemCodes.ObjectDefinitionConflict);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Publish_WhenDraftIsValid_PersistsPublishedVersionAuditMetadata()
    {
        ObjectDefinition definition = DraftWithOneSave();
        _repository.GetByIdForWorkspaceAsync(definition.Id, WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(definition);
        PublishObjectDefinitionHandler sut = new(_currentUser, _repository, _unitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new PublishObjectDefinitionCommand(definition.Id.Value, ExpectedDraftVersion: 2),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ObjectDefinitionStatus.Published);
        result.Value.LatestPublishedVersionNumber.Should().Be(1);
        result.Value.LatestPublishedVersion.Should().NotBeNull();
        result.Value.LatestPublishedVersion!.PublishedByUserId.Should().Be(UserId);
        result.Value.LatestPublishedVersion.Fields.Should().ContainSingle(field => field.FieldKey == "name");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_WhenUserScopeIsMissing_ReturnsForbiddenWithoutCommit()
    {
        _currentUser.UserId = null;
        PublishObjectDefinitionHandler sut = new(_currentUser, _repository, _unitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new PublishObjectDefinitionCommand(Guid.NewGuid(), ExpectedDraftVersion: 1),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ProblemCode.Should().Be(ObjectsProblemCodes.UserScopeRequired);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task GetObjectDefinition_WhenRepositoryReturnsNull_ReturnsNotFound()
    {
        GetObjectDefinitionHandler sut = new(_currentUser, _repository);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new GetObjectDefinitionQuery(Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ProblemCode.Should().Be(ObjectsProblemCodes.ObjectDefinitionNotFound);
    }

    [Fact]
    public async Task ListObjectDefinitions_WhenWorkspaceScoped_ReturnsPagedDeterministicItems()
    {
        ObjectDefinition first = CreateDraft("Customer", "customer");
        ObjectDefinition second = CreateDraft("Invoice", "invoice");
        _repository.CountForWorkspaceAsync(WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(2);
        _repository.ListForWorkspaceAsync(WorkspaceId, 1, 20, Arg.Any<CancellationToken>())
            .Returns([first, second]);
        ListObjectDefinitionsHandler sut = new(_currentUser, _repository);

        Result<PagedResult<ObjectDefinitionListItemDto>> result = await sut.Handle(
            new ListObjectDefinitionsQuery(1, 20),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(20);
        result.Value.Items.Select(item => item.ObjectKey).Should().Equal("customer", "invoice");
    }

    private static ObjectDefinition DraftWithOneSave()
    {
        ObjectDefinition definition = CreateDraft("Customer", "customer");
        definition.SaveDraft(
            "Customer",
            [FieldSpec("name", "Name", order: 0)],
            expectedDraftVersion: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        return definition;
    }

    private static ObjectDefinition CreateDraft(string name, string key)
    {
        Result<ObjectDefinition> result = ObjectDefinition.CreateDraft(
            WorkspaceId,
            name,
            ObjectDefinitionKey.Create(key).Value,
            DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static ObjectFieldDefinitionInput FieldInput(string key, string label) =>
        new(key, label);

    private static ObjectFieldDefinitionSpec FieldSpec(string key, string label, int order) =>
        new(key, label, order);

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public Guid? workspaceId { get; set; }
    }
}
