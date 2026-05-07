using Axis.DataModeling.Application.Queries.GetModels;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Queries;

public class GetModelsHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private GetModelsHandler CreateHandler() => new(_modelRepo);

    [Fact]
    public async Task Returns_model_dtos_for_org()
    {
        var models = new List<DataModel>
        {
            DataModel.Create("Invoice", "Billing", null, null, OrgId),
            DataModel.Create("Contact", null, null, null, OrgId),
        };
        _modelRepo.GetAllAsync(OrgId).Returns(models);

        var result = await CreateHandler().Handle(new GetModelsQuery(OrgId), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().Contain(m => m.Name == "Invoice" && m.Description == "Billing");
    }

    [Fact]
    public async Task Each_dto_contains_correct_field_count()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        _modelRepo.GetAllAsync(OrgId).Returns(new List<DataModel> { model });

        var result = await CreateHandler().Handle(new GetModelsQuery(OrgId), CancellationToken.None);

        // 3 system fields auto-created
        result.Single().FieldCount.Should().Be(3);
    }

    [Fact]
    public async Task Empty_org_returns_empty_list()
    {
        _modelRepo.GetAllAsync(OrgId).Returns(new List<DataModel>());

        var result = await CreateHandler().Handle(new GetModelsQuery(OrgId), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
