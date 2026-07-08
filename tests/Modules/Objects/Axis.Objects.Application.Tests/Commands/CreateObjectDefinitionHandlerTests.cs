using Axis.Objects.Application;
using Axis.Objects.Application.Commands.CreateObjectDefinition;
using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Objects.Application.Tests.Commands;

public sealed class CreateObjectDefinitionHandlerTests
{
    private readonly ObjectDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task CreateUnpublished_WhenWorkspaceScopedKeyIsAvailable_AddsUnpublishedDefinitionAndReturnsRevisionOne()
    {
        _context.Repository.ObjectKeyExistsAsync(
                ObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<ObjectDefinitionKey>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(false);

        CreateObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new CreateObjectDefinitionCommand("Customer"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.WorkspaceId.Should().Be(ObjectDefinitionHandlerTestContext.WorkspaceId);
        result.Value.ObjectKey.Should().Be("customer");
        result.Value.Revision.Should().Be(1);
        result.Value.Status.Should().Be(ObjectDefinitionStatus.Unpublished);
        await _context.Repository.Received(1).AddAsync(
            Arg.Any<ObjectDefinition>(),
            Arg.Any<CancellationToken>());
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateUnpublished_WhenWorkspaceScopeIsMissing_ReturnsForbiddenWithoutMutation()
    {
        _context.CurrentUser.workspaceId = null;
        CreateObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new CreateObjectDefinitionCommand("Customer"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ProblemCode.Should().Be(ObjectsProblemCodes.WorkspaceScopeRequired);
        await _context.Repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _context.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task CreateUnpublished_WhenNameNeedsNormalization_DerivesServerOwnedObjectKey()
    {
        _context.Repository.ObjectKeyExistsAsync(
                ObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Is<ObjectDefinitionKey>(key => key.Value == "customer_account_2026"),
                null,
                Arg.Any<CancellationToken>())
            .Returns(false);
        CreateObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new CreateObjectDefinitionCommand("Customer Account 2026!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ObjectKey.Should().Be("customer_account_2026");
    }

    [Fact]
    public async Task CreateUnpublished_WhenObjectKeyAlreadyExists_ReturnsConflict()
    {
        _context.Repository.ObjectKeyExistsAsync(
                ObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<ObjectDefinitionKey>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(true);
        CreateObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new CreateObjectDefinitionCommand("Customer"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ProblemCode.Should().Be(ObjectsProblemCodes.ObjectKeyAlreadyExists);
    }
}
