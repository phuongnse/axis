using Axis.DataModeling.Application.Queries.GetDataClass;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Queries;

public class GetDataClassHandlerTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private readonly IDataClassRepository _dataClassRepo = Substitute.For<IDataClassRepository>();

    private GetDataClassHandler CreateHandler() => new(_dataClassRepo);

    [Fact]
    public async Task Handle_WhenDataClassExists_ReturnsFieldDetails()
    {
        DataClass dataClass = DataClass.Create("Address", "Postal address", WorkspaceId, "user-123");
        dataClass.AddField("street", "Street", FieldType.Text, true, new TextFieldConfig());
        _dataClassRepo.GetByIdAsync(dataClass.Id, WorkspaceId, Arg.Any<CancellationToken>()).Returns(dataClass);

        Result<DataClassDetailDto> result = await CreateHandler().Handle(
            new GetDataClassQuery(dataClass.Id, WorkspaceId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Address");
        result.Value.Fields.Should().Contain(f => f.Name == "street" && f.IsRequired);
    }

    [Fact]
    public async Task Handle_WhenDataClassDoesNotExist_ReturnsNotFound()
    {
        _dataClassRepo.GetByIdAsync(Arg.Any<Guid>(), WorkspaceId, Arg.Any<CancellationToken>())
            .Returns((DataClass?)null);

        Result<DataClassDetailDto> result = await CreateHandler().Handle(
            new GetDataClassQuery(Guid.NewGuid(), WorkspaceId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
