using Axis.DataModeling.Application.Commands.AddField;
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

public class AddFieldHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private AddFieldHandler CreateHandler() => new(_modelRepo, _uow);

    private static DataModel MakeModel() => DataModel.Create("Invoice", null, null, null, OrgId, UserId);

    [Fact]
    public async Task Happy_path_adds_text_field()
    {
        DataModel model = MakeModel();
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        Result<Guid> result = await CreateHandler().Handle(
            new AddFieldCommand(model.Id, OrgId, "amount", "Amount", FieldType.Text, false, new TextFieldConfig()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        model.Fields.Should().Contain(f => f.Name == "amount");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Model_not_found_returns_not_found()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        Result<Guid> result = await CreateHandler().Handle(
            new AddFieldCommand(Guid.NewGuid(), OrgId, "amount", "Amount", FieldType.Text, false, new TextFieldConfig()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }
}
