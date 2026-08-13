using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Queries.GetBusinessObjectDefinition;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Identity.Contracts;
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
    public async Task GetBusinessObjectDefinition_WhenNonBuilderRequestsUnpublishedDefinition_ReturnsForbidden()
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.UnpublishedWithOneSave();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        _context.Authorization.AuthorizeAsync(
                Arg.Any<Guid>(),
                Arg.Any<SubjectReference>(),
                Arg.Any<CancellationToken>())
            .Returns(WorkspaceProductBuilderDecision.Denied);
        GetBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Authorization,
            _context.Repository);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new GetBusinessObjectDefinitionQuery(definition.Id.Value),
            TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task GetBusinessObjectDefinition_WhenBuilderAuthorizationUnavailable_ReturnsUnavailable()
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.UnpublishedWithOneSave();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        _context.Authorization.AuthorizeAsync(
                Arg.Any<Guid>(),
                Arg.Any<SubjectReference>(),
                Arg.Any<CancellationToken>())
            .Returns(WorkspaceProductBuilderDecision.Unavailable);
        GetBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Authorization,
            _context.Repository);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new GetBusinessObjectDefinitionQuery(definition.Id.Value),
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Unavailable);
        result.ProblemCode.Should().Be(BusinessObjectsProblemCodes.AuthorizationUnavailable);
    }

    [Fact]
    public async Task GetBusinessObjectDefinition_WhenNonBuilderHasPublishedRead_ReturnsRuntimeProjection()
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.UnpublishedWithOneSave();
        definition.Publish(
            expectedRevision: 2,
            Axis.BusinessObjects.Domain.ValueObjects.SubjectReference.Human(BusinessObjectDefinitionHandlerTestContext.UserId),
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        _context.Repository.GetByIdForWorkspaceAsync(
                definition.Id,
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        _context.Authorization.AuthorizeAsync(
                Arg.Any<Guid>(),
                Arg.Any<SubjectReference>(),
                Arg.Any<CancellationToken>())
            .Returns(WorkspaceProductBuilderDecision.Denied);
        _context.Authorization.AuthorizeAsync(
                Arg.Any<ProductAuthorizationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ProductAuthorizationRequest>().ActionKey == BusinessObjectProductActions.DefinitionReadPublished
                ? new ProductAuthorizationDecision(true, ProductActionScope.None)
                : ProductAuthorizationDecision.Denied);
        GetBusinessObjectDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Authorization,
            _context.Repository);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new GetBusinessObjectDefinitionQuery(definition.Id.Value),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(BusinessObjectDefinitionStatus.Published);
        result.Value.Actions.CanSave.Should().BeFalse();
        result.Value.Actions.CanPublish.Should().BeFalse();
    }
}
