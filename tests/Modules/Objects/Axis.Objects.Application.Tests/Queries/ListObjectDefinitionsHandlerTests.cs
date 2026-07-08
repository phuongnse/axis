using Axis.Objects.Application;
using Axis.Objects.Application.Queries.ListObjectDefinitions;
using Axis.Objects.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Objects.Application.Tests.Queries;

public sealed class ListObjectDefinitionsHandlerTests
{
    private readonly ObjectDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task ListObjectDefinitions_WhenWorkspaceScoped_ReturnsPagedDeterministicItems()
    {
        ObjectDefinition first = ObjectDefinitionHandlerTestContext.CreateUnpublished("Customer", "customer");
        ObjectDefinition second = ObjectDefinitionHandlerTestContext.CreateUnpublished("Invoice", "invoice");
        _context.Repository.CountForWorkspaceAsync(
                ObjectDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(2);
        _context.Repository.ListForWorkspaceAsync(
                ObjectDefinitionHandlerTestContext.WorkspaceId,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns([first, second]);
        ListObjectDefinitionsHandler sut = new(_context.CurrentUser, _context.Repository);

        Result<PagedResult<ObjectDefinitionListItemDto>> result = await sut.Handle(
            new ListObjectDefinitionsQuery(1, 20),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(20);
        result.Value.Items.Select(item => item.ObjectKey).Should().Equal("customer", "invoice");
    }
}
