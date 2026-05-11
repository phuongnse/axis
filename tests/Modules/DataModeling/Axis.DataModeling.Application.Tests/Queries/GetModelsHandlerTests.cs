using Axis.DataModeling.Application.Queries.GetModels;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Queries;

public class GetModelsHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private GetModelsHandler CreateHandler() => new(_modelRepo);

    [Fact]
    public async Task GetModels_WithExistingModels_ReturnsPagedDtos()
    {
        List<DataModel> models =
        [
            DataModel.Create("Invoice", "Billing", null, null, OrgId, UserId),
            DataModel.Create("Contact", null, null, null, OrgId, UserId),
        ];
        _modelRepo.GetPagedAsync(OrgId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((models, 2));

        PagedResult<ModelSummaryDto> result = await CreateHandler()
            .Handle(new GetModelsQuery(OrgId, 1, 20), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.Items.Should().Contain(m => m.Name == "Invoice" && m.Description == "Billing");
    }

    [Fact]
    public async Task GetModels_WithModels_EachDtoContainsCorrectFieldCount()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, OrgId, UserId);
        _modelRepo.GetPagedAsync(OrgId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((new List<DataModel> { model }, 1));

        PagedResult<ModelSummaryDto> result = await CreateHandler()
            .Handle(new GetModelsQuery(OrgId, 1, 20), CancellationToken.None);

        // 3 system fields auto-created
        result.Items.Single().FieldCount.Should().Be(3);
    }

    [Fact]
    public async Task GetModels_EmptyOrg_ReturnsEmptyPage()
    {
        _modelRepo.GetPagedAsync(OrgId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((new List<DataModel>(), 0));

        PagedResult<ModelSummaryDto> result = await CreateHandler()
            .Handle(new GetModelsQuery(OrgId, 1, 20), CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetModels_PageSizeExceedsCap_ClampsTo100()
    {
        _modelRepo.GetPagedAsync(OrgId, 1, 100, Arg.Any<CancellationToken>())
            .Returns((new List<DataModel>(), 0));

        await CreateHandler().Handle(new GetModelsQuery(OrgId, 1, 999), CancellationToken.None);

        await _modelRepo.Received(1)
            .GetPagedAsync(OrgId, 1, 100, Arg.Any<CancellationToken>());
    }
}
