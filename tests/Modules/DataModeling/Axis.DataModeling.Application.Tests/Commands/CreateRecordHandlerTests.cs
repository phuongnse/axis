using Axis.DataModeling.Application.Commands.CreateRecord;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.DataModeling.Application.Tests.Commands;

public class CreateRecordHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IDataRecordRepository _recordRepo = Substitute.For<IDataRecordRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private CreateRecordHandler CreateHandler() => new(_modelRepo, _recordRepo, _uow);

    [Fact]
    public async Task Happy_path_creates_record_and_returns_id()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        var data = new Dictionary<string, object?> { ["amount"] = 100 };

        var result = await CreateHandler().Handle(
            new CreateRecordCommand(model.Id, OrgId, data),
            CancellationToken.None);

        result.Should().NotBeEmpty();
        await _recordRepo.Received(1).AddAsync(Arg.Any<DataRecord>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Model_not_found_throws_validation_exception()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        var act = async () => await CreateHandler().Handle(
            new CreateRecordCommand(Guid.NewGuid(), OrgId, new Dictionary<string, object?>()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*not found*");
    }
}
