using Axis.DataModeling.Application.Queries.GetModel;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Queries;

public class GetModelHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private GetModelHandler CreateHandler() => new(_modelRepo);

    [Fact]
    public async Task Returns_model_detail_with_all_fields()
    {
        DataModel model = DataModel.Create("Invoice", "desc", null, null, OrgId, UserId);
        model.AddField("amount", "Amount", FieldType.Number, true, new NumberFieldConfig(Min: 0));
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        Result<ModelDetailDto> result = await CreateHandler().Handle(new GetModelQuery(model.Id, OrgId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Invoice");
        // 3 system fields + 1 custom
        result.Value.Fields.Should().HaveCount(4);
        result.Value.Fields.Should().Contain(f => f.Name == "amount" && f.IsRequired);
    }

    [Fact]
    public async Task Returns_not_found_when_model_does_not_exist()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).Returns((DataModel?)null);

        Result<ModelDetailDto> result = await CreateHandler().Handle(
            new GetModelQuery(Guid.NewGuid(), OrgId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
