using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Commands.CreateBusinessObjectDefinition;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests.Commands;

public sealed class CreateBusinessObjectDefinitionHandlerTests
{
    private readonly BusinessObjectDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task CreateUnpublished_WhenWorkspaceScopedKeyIsAvailable_AddsUnpublishedDefinitionAndReturnsRevisionOne()
    {
        _context.Repository.ObjectKeyExistsAsync(
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<BusinessObjectDefinitionKey>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(false);

        CreateBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new CreateBusinessObjectDefinitionCommand("Customer"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.WorkspaceId.Should().Be(BusinessObjectDefinitionHandlerTestContext.WorkspaceId);
        result.Value.ObjectKey.Should().Be("customer");
        result.Value.Revision.Should().Be(1);
        result.Value.Status.Should().Be(BusinessObjectDefinitionStatus.Unpublished);
        await _context.Repository.Received(1).AddAsync(
            Arg.Any<BusinessObjectDefinition>(),
            Arg.Any<CancellationToken>());
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateUnpublished_WhenWorkspaceScopeIsMissing_ReturnsForbiddenWithoutMutation()
    {
        _context.CurrentUser.workspaceId = null;
        CreateBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new CreateBusinessObjectDefinitionCommand("Customer"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ProblemCode.Should().Be(BusinessObjectsProblemCodes.WorkspaceScopeRequired);
        await _context.Repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _context.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task CreateUnpublished_WhenNameNeedsNormalization_DerivesServerOwnedObjectKey()
    {
        _context.Repository.ObjectKeyExistsAsync(
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Is<BusinessObjectDefinitionKey>(key => key.Value == "customer_account_2026"),
                null,
                Arg.Any<CancellationToken>())
            .Returns(false);
        CreateBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new CreateBusinessObjectDefinitionCommand("Customer Account 2026!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ObjectKey.Should().Be("customer_account_2026");
    }

    [Fact]
    public async Task CreateUnpublished_WhenObjectKeyAlreadyExists_ReturnsConflict()
    {
        _context.Repository.ObjectKeyExistsAsync(
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<BusinessObjectDefinitionKey>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(true);
        CreateBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.UnitOfWork);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new CreateBusinessObjectDefinitionCommand("Customer"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ProblemCode.Should().Be(BusinessObjectsProblemCodes.ObjectKeyAlreadyExists);
    }
}
