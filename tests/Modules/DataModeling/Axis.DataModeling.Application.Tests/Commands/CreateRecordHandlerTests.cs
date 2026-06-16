using Axis.DataModeling.Application.Commands.CreateRecord;
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

public class CreateRecordHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IDataRecordRepository _recordRepo = Substitute.For<IDataRecordRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TenantId = Guid.NewGuid();
    private const string UserId = "user-123";

    private CreateRecordHandler CreateHandler() => new(_modelRepo, _recordRepo, _uow);

    [Fact]
    public async Task CreateRecord_WhenModelExists_CreatesRecordAndReturnsId()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        _modelRepo.GetByIdAsync(model.Id, TenantId).Returns(model);

        Dictionary<string, object?> data = new() { ["amount"] = 100 };

        Result<Guid> result = await CreateHandler().Handle(
            new CreateRecordCommand(model.Id, TenantId, data, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        await _recordRepo.Received(1).AddAsync(Arg.Any<DataRecord>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRecord_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), TenantId).ReturnsNull();

        Result<Guid> result = await CreateHandler().Handle(
            new CreateRecordCommand(Guid.NewGuid(), TenantId, new Dictionary<string, object?>(), UserId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task CreateRecord_WhenRequiredFieldMissing_ReturnsFieldValidationError()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        model.AddField("title", "Title", FieldType.Text, required: true, new TextFieldConfig());
        _modelRepo.GetByIdAsync(model.Id, TenantId).Returns(model);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateRecordCommand(model.Id, TenantId, new Dictionary<string, object?>(), UserId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.FieldValidation);
        result.FieldErrors.Should().ContainKey("title");
        await _recordRepo.DidNotReceive().AddAsync(Arg.Any<DataRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRecord_WhenFieldDataValid_CreatesRecord()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        model.AddField("title", "Title", FieldType.Text, required: true, new TextFieldConfig(MaxLength: 100));
        _modelRepo.GetByIdAsync(model.Id, TenantId).Returns(model);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateRecordCommand(model.Id, TenantId, new Dictionary<string, object?> { ["title"] = "Test Invoice" }, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CreateRecord_WhenModelBelongsToAnotherTenant_ReturnsNotFound()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        _modelRepo.GetByIdAsync(model.Id, TenantId).Returns(model);

        Guid otherTenantId = Guid.NewGuid();
        Result<Guid> result = await CreateHandler().Handle(
            new CreateRecordCommand(model.Id, otherTenantId, new Dictionary<string, object?>(), UserId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
