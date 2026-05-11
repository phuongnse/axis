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

    private static readonly Guid OrgId = Guid.NewGuid();

    private GetDataClassesHandler CreateHandler() => new(_dataClassRepo);

    [Fact]
    public async Task GetDataClasses_WithExistingClasses_ReturnsPagedDtos()
    {
        List<DataClass> classes =
        [
            DataClass.Create("Address", "Reusable address type", OrgId),
            DataClass.Create("Contact", null, OrgId),
        ];
        _dataClassRepo.GetPagedAsync(OrgId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((classes, 2));

        PagedResult<DataClassSummaryDto> result = await CreateHandler()
            .Handle(new GetDataClassesQuery(OrgId, 1, 20), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Items.Should().Contain(c => c.Name == "Address");
    }

    [Fact]
    public async Task GetDataClasses_EmptyOrg_ReturnsEmptyPage()
    {
        _dataClassRepo.GetPagedAsync(OrgId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((new List<DataClass>(), 0));

        PagedResult<DataClassSummaryDto> result = await CreateHandler()
            .Handle(new GetDataClassesQuery(OrgId, 1, 20), CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetDataClasses_PageSizeExceedsCap_ClampsTo100()
    {
        _dataClassRepo.GetPagedAsync(OrgId, 1, 100, Arg.Any<CancellationToken>())
            .Returns((new List<DataClass>(), 0));

        await CreateHandler().Handle(new GetDataClassesQuery(OrgId, 1, 500), CancellationToken.None);

        await _dataClassRepo.Received(1)
            .GetPagedAsync(OrgId, 1, 100, Arg.Any<CancellationToken>());
    }
}
