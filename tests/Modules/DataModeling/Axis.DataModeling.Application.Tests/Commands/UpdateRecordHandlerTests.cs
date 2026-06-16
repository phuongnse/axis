using Axis.DataModeling.Application.Commands.UpdateRecord;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.DataModeling.Application.Tests.Commands;

public class UpdateRecordHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IDataRecordRepository _recordRepo = Substitute.For<IDataRecordRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private const string UserId = "user-123";

    private UpdateRecordHandler CreateHandler() => new(_modelRepo, _recordRepo, _uow);

    private static DataModel SimpleModel()
        => DataModel.Create("Invoice", null, null, null, TeamAccountId, UserId);

    [Fact]
    public async Task UpdateRecord_WhenModelAndRecordExist_UpdatesDataAndSaves()
    {
        DataModel model = SimpleModel();
        DataRecord record = DataRecord.Create(model.Id, TeamAccountId, new Dictionary<string, object?> { ["name"] = "Old" }, UserId);
        _modelRepo.GetByIdAsync(model.Id, TeamAccountId).Returns(model);
        _recordRepo.GetByIdAsync(record.Id, model.Id, TeamAccountId).Returns(record);

        Dictionary<string, object?> newData = new() { ["name"] = "New" };
        Result result = await CreateHandler().Handle(
            new UpdateRecordCommand(record.Id, model.Id, TeamAccountId, newData),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        record.Data["name"].Should().Be("New");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateRecord_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), TeamAccountId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new UpdateRecordCommand(Guid.NewGuid(), Guid.NewGuid(), TeamAccountId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task UpdateRecord_WhenRecordNotFound_ReturnsNotFound()
    {
        DataModel model = SimpleModel();
        _modelRepo.GetByIdAsync(model.Id, TeamAccountId).Returns(model);
        _recordRepo.GetByIdAsync(Arg.Any<Guid>(), model.Id, TeamAccountId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new UpdateRecordCommand(Guid.NewGuid(), model.Id, TeamAccountId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("Record");
    }

    [Fact]
    public async Task UpdateRecord_WhenModelBelongsToAnotherTeamAccount_ReturnsNotFound()
    {
        DataModel model = SimpleModel();
        _modelRepo.GetByIdAsync(model.Id, TeamAccountId).Returns(model);

        Guid otherTeamAccountId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new UpdateRecordCommand(Guid.NewGuid(), model.Id, otherTeamAccountId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _modelRepo.Received(1).GetByIdAsync(model.Id, otherTeamAccountId);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateRecord_WhenRequiredFieldMissing_ReturnsFieldValidationError()
    {
        DataModel model = SimpleModel();
        model.AddField("title", "Title", FieldType.Text, required: true, new TextFieldConfig());
        DataRecord record = DataRecord.Create(model.Id, TeamAccountId, new Dictionary<string, object?> { ["title"] = "Old" }, UserId);
        _modelRepo.GetByIdAsync(model.Id, TeamAccountId).Returns(model);
        _recordRepo.GetByIdAsync(record.Id, model.Id, TeamAccountId).Returns(record);

        Result result = await CreateHandler().Handle(
            new UpdateRecordCommand(record.Id, model.Id, TeamAccountId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.FieldValidation);
        result.FieldErrors.Should().ContainKey("title");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
