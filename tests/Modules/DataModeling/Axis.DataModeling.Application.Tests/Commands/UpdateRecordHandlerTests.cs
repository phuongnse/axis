using Axis.DataModeling.Application.Commands.UpdateRecord;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class UpdateRecordHandlerTests
{
    private readonly IDataRecordRepository _recordRepo = Substitute.For<IDataRecordRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();

    private UpdateRecordHandler CreateHandler() => new(_recordRepo, _uow);

    [Fact]
    public async Task Happy_path_updates_data_and_saves()
    {
        var record = DataRecord.Create(ModelId, OrgId, new Dictionary<string, object?> { ["name"] = "Old" });
        _recordRepo.GetByIdAsync(record.Id, ModelId, OrgId).Returns(record);

        var newData = new Dictionary<string, object?> { ["name"] = "New" };
        await CreateHandler().Handle(
            new UpdateRecordCommand(record.Id, ModelId, OrgId, newData),
            CancellationToken.None);

        record.Data["name"].Should().Be("New");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Record_not_found_throws()
    {
        _recordRepo.GetByIdAsync(Arg.Any<Guid>(), ModelId, OrgId).Returns((DataRecord?)null);

        var act = async () => await CreateHandler().Handle(
            new UpdateRecordCommand(Guid.NewGuid(), ModelId, OrgId, new Dictionary<string, object?>()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*not found*");
    }
}
