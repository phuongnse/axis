using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Queries.ListBusinessObjectDefinitions;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Identity.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using FluentValidation.Results;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests.Queries;

public sealed class ListBusinessObjectDefinitionsHandlerTests
{
    [Theory]
    [InlineData(null, CollectionSortDirection.Descending)]
    [InlineData(BusinessObjectDefinitionSortField.Name, null)]
    public void Validator_WhenSortPairIsIncomplete_RejectsQuery(
        BusinessObjectDefinitionSortField? sortBy,
        CollectionSortDirection? sortDirection)
    {
        ListBusinessObjectDefinitionsQueryValidator validator = new();

        ValidationResult result = validator.Validate(new ListBusinessObjectDefinitionsQuery(
            1,
            20,
            SortBy: sortBy,
            SortDirection: sortDirection));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(ListBusinessObjectDefinitionsQuery.SortDirection));
    }

    [Fact]
    public void Validator_WhenSortValuesAreUndefined_RejectsQuery()
    {
        ListBusinessObjectDefinitionsQueryValidator validator = new();

        ValidationResult result = validator.Validate(new ListBusinessObjectDefinitionsQuery(
            1,
            20,
            SortBy: (BusinessObjectDefinitionSortField)999,
            SortDirection: (CollectionSortDirection)999));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.PropertyName).Should().Contain([
            nameof(ListBusinessObjectDefinitionsQuery.SortBy),
            nameof(ListBusinessObjectDefinitionsQuery.SortDirection),
        ]);
    }

    [Fact]
    public async Task ListBusinessObjectDefinitions_WhenOnlyPublishedReadIsAllowed_FiltersBeforeMaterialization()
    {
        _context.Authorization.AuthorizeAsync(
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                _context.CurrentSubject.Subject,
                Arg.Any<CancellationToken>())
            .Returns(WorkspaceProductBuilderDecision.Denied);
        _context.Authorization.AuthorizeAsync(
                Arg.Is<ProductAuthorizationRequest>(request =>
                    request.ActionKey == BusinessObjectProductActions.DefinitionReadPublished),
                Arg.Any<CancellationToken>())
            .Returns(new ProductAuthorizationDecision(true, ProductActionScope.None));
        _context.Repository.CountForWorkspaceAsync(
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                null,
                true,
                Arg.Any<CancellationToken>())
            .Returns(0);
        _context.Repository.ListForWorkspaceAsync(
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                1,
                20,
                null,
                true,
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns([]);
        ListBusinessObjectDefinitionsHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Authorization,
            _context.Repository);

        Result<PagedResult<BusinessObjectDefinitionListItemDto>> result = await sut.Handle(
            new ListBusinessObjectDefinitionsQuery(1, 20, CorrelationId: "published-list"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _context.Repository.Received(1).ListForWorkspaceAsync(
            BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
            1,
            20,
            null,
            true,
            null,
            null,
            Arg.Any<CancellationToken>());
    }

    private readonly BusinessObjectDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task ListBusinessObjectDefinitions_WhenWorkspaceScoped_ReturnsPagedDeterministicItems()
    {
        BusinessObjectDefinition first = BusinessObjectDefinitionHandlerTestContext.CreateUnpublished("Customer", "customer");
        BusinessObjectDefinition second = BusinessObjectDefinitionHandlerTestContext.CreateUnpublished("Invoice", "invoice");
        _context.Repository.CountForWorkspaceAsync(
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                null,
                false,
                Arg.Any<CancellationToken>())
            .Returns(2);
        _context.Repository.ListForWorkspaceAsync(
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                1,
                20,
                null,
                false,
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns([first, second]);
        ListBusinessObjectDefinitionsHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Authorization,
            _context.Repository);

        Result<PagedResult<BusinessObjectDefinitionListItemDto>> result = await sut.Handle(
            new ListBusinessObjectDefinitionsQuery(1, 20),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(20);
        result.Value.Items.Select(item => item.ObjectKey).Should().Equal("customer", "invoice");
    }

    [Fact]
    public async Task ListBusinessObjectDefinitions_WhenSortIsRequested_ForwardsWholeDatasetSort()
    {
        _context.Repository.CountForWorkspaceAsync(
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                "customer",
                false,
                Arg.Any<CancellationToken>())
            .Returns(0);
        _context.Repository.ListForWorkspaceAsync(
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                2,
                10,
                "customer",
                false,
                BusinessObjectDefinitionSortField.Name,
                CollectionSortDirection.Descending,
                Arg.Any<CancellationToken>())
            .Returns([]);
        ListBusinessObjectDefinitionsHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Authorization,
            _context.Repository);

        Result<PagedResult<BusinessObjectDefinitionListItemDto>> result = await sut.Handle(
            new ListBusinessObjectDefinitionsQuery(
                2,
                10,
                "customer",
                SortBy: BusinessObjectDefinitionSortField.Name,
                SortDirection: CollectionSortDirection.Descending),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _context.Repository.Received(1).ListForWorkspaceAsync(
            BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
            2,
            10,
            "customer",
            false,
            BusinessObjectDefinitionSortField.Name,
            CollectionSortDirection.Descending,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListBusinessObjectDefinitions_WhenBuilderAndPublishedReadAreDenied_ReturnsForbidden()
    {
        _context.Authorization.AuthorizeAsync(
                Arg.Any<Guid>(),
                Arg.Any<SubjectReference>(),
                Arg.Any<CancellationToken>())
            .Returns(WorkspaceProductBuilderDecision.Denied);
        _context.Authorization.AuthorizeAsync(
                Arg.Any<ProductAuthorizationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(ProductAuthorizationDecision.Denied);
        ListBusinessObjectDefinitionsHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Authorization,
            _context.Repository);

        Result<PagedResult<BusinessObjectDefinitionListItemDto>> result = await sut.Handle(
            new ListBusinessObjectDefinitionsQuery(1, 20),
            TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        await _context.Repository.DidNotReceive().ListForWorkspaceAsync(
            Arg.Any<Guid>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<BusinessObjectDefinitionSortField?>(),
            Arg.Any<CollectionSortDirection?>(),
            TestContext.Current.CancellationToken);
    }
}
