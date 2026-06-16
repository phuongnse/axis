using Axis.DataModeling.Application.Queries.GetDataClasses;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Queries;

public class GetDataClassesHandlerTests
{
    private readonly IDataClassRepository _dataClassRepo = Substitute.For<IDataClassRepository>();

    private static readonly Guid TenantId = Guid.NewGuid();
    private const string UserId = "user-123";

    private GetDataClassesHandler CreateHandler() => new(_dataClassRepo);

    [Fact]
    public async Task GetDataClasses_WithExistingClasses_ReturnsPagedDtos()
    {
        List<DataClass> classes =
        [
            DataClass.Create("Address", "Reusable address type", TenantId, UserId),
            DataClass.Create("Contact", null, TenantId, UserId),
        ];
        _dataClassRepo.GetPagedAsync(TenantId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((classes, 2));

        PagedResult<DataClassSummaryDto> result = await CreateHandler()
            .Handle(new GetDataClassesQuery(TenantId, 1, 20), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Items.Should().Contain(c => c.Name == "Address");
    }

    [Fact]
    public async Task GetDataClasses_EmptyTenant_ReturnsEmptyPage()
    {
        _dataClassRepo.GetPagedAsync(TenantId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((new List<DataClass>(), 0));

        PagedResult<DataClassSummaryDto> result = await CreateHandler()
            .Handle(new GetDataClassesQuery(TenantId, 1, 20), CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetDataClasses_PageSizeExceedsCap_ClampsTo100()
    {
        _dataClassRepo.GetPagedAsync(TenantId, 1, 100, Arg.Any<CancellationToken>())
            .Returns((new List<DataClass>(), 0));

        await CreateHandler().Handle(new GetDataClassesQuery(TenantId, 1, 500), CancellationToken.None);

        await _dataClassRepo.Received(1)
            .GetPagedAsync(TenantId, 1, 100, Arg.Any<CancellationToken>());
    }
}
