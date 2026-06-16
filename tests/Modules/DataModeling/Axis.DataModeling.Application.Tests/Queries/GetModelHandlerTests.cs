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
    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private const string UserId = "user-123";

    private GetModelHandler CreateHandler() => new(_modelRepo);

    [Fact]
    public async Task GetModel_WhenModelExists_ReturnsModelDetailWithAllFields()
    {
        DataModel model = DataModel.Create("Invoice", "desc", null, null, TeamAccountId, UserId);
        model.AddField("amount", "Amount", FieldType.Number, true, new NumberFieldConfig(Min: 0));
        _modelRepo.GetByIdAsync(model.Id, TeamAccountId).Returns(model);

        Result<ModelDetailDto> result = await CreateHandler().Handle(new GetModelQuery(model.Id, TeamAccountId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Invoice");
        // 3 system fields + 1 custom
        result.Value.Fields.Should().HaveCount(4);
        result.Value.Fields.Should().Contain(f => f.Name == "amount" && f.IsRequired);
    }

    [Fact]
    public async Task GetModel_WhenModelDoesNotExist_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), TeamAccountId).Returns((DataModel?)null);

        Result<ModelDetailDto> result = await CreateHandler().Handle(
            new GetModelQuery(Guid.NewGuid(), TeamAccountId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
