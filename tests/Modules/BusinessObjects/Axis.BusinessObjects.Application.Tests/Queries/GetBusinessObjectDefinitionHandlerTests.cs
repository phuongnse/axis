using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Queries.GetBusinessObjectDefinition;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests.Queries;

public sealed class GetBusinessObjectDefinitionHandlerTests
{
    private readonly BusinessObjectDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task GetBusinessObjectDefinition_WhenRepositoryReturnsNull_ReturnsNotFound()
    {
        GetBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Repository);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new GetBusinessObjectDefinitionQuery(Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ProblemCode.Should().Be(BusinessObjectsProblemCodes.BusinessObjectDefinitionNotFound);
    }

    [Fact]
    public async Task GetBusinessObjectDefinition_WhenExactManageDenied_ProjectsLifecycleActionsAsFalse()
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.UnpublishedWithOneSave();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        _context.Authorization.AuthorizeAsync(
                Arg.Any<ProductAuthorizationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ProductAuthorizationRequest>().ActionKey == BusinessObjectProductActions.DefinitionRead
                ? new ProductAuthorizationDecision(true, ProductActionScope.None)
                : ProductAuthorizationDecision.Denied);
        GetBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Repository);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new GetBusinessObjectDefinitionQuery(definition.Id.Value),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Actions.CanSave.Should().BeFalse();
        result.Value.Actions.CanPublish.Should().BeFalse();
    }

    [Fact]
    public async Task GetBusinessObjectDefinition_WhenManageDecisionUnavailable_ReturnsUnavailable()
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.UnpublishedWithOneSave();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        _context.Authorization.AuthorizeAsync(
                Arg.Any<ProductAuthorizationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ProductAuthorizationRequest>().ActionKey == BusinessObjectProductActions.DefinitionRead
                ? new ProductAuthorizationDecision(true, ProductActionScope.None)
                : ProductAuthorizationDecision.Unavailable);
        GetBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Repository);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new GetBusinessObjectDefinitionQuery(definition.Id.Value),
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Unavailable);
        result.ProblemCode.Should().Be(BusinessObjectsProblemCodes.AuthorizationUnavailable);
    }
}
