using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Queries.ListBusinessObjectDefinitions;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests.Queries;

public sealed class ListBusinessObjectDefinitionsHandlerTests
{
    private readonly BusinessObjectDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task ListBusinessObjectDefinitions_WhenWorkspaceScoped_ReturnsPagedDeterministicItems()
    {
        BusinessObjectDefinition first = BusinessObjectDefinitionHandlerTestContext.CreateUnpublished("Customer", "customer");
        BusinessObjectDefinition second = BusinessObjectDefinitionHandlerTestContext.CreateUnpublished("Invoice", "invoice");
        _context.Repository.CountForWorkspaceAsync(
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(2);
        _context.Repository.ListForWorkspaceAsync(
                BusinessObjectDefinitionHandlerTestContext.WorkspaceId,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns([first, second]);
        ListBusinessObjectDefinitionsHandler sut = new(_context.CurrentUser, _context.Repository);

        Result<PagedResult<BusinessObjectDefinitionListItemDto>> result = await sut.Handle(
            new ListBusinessObjectDefinitionsQuery(1, 20),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(20);
        result.Value.Items.Select(item => item.ObjectKey).Should().Equal("customer", "invoice");
    }
}
