using Axis.DataModeling.Application.Commands.AddField;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.DataModeling.Application.Tests.Commands;

public class AddFieldHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private AddFieldHandler CreateHandler() => new(_modelRepo, _uow);

    private static DataModel MakeModel() => DataModel.Create("Invoice", null, null, null, OrgId);

    [Fact]
    public async Task Happy_path_adds_text_field()
    {
        var model = MakeModel();
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        await CreateHandler().Handle(
            new AddFieldCommand(model.Id, OrgId, "amount", "Amount", FieldType.Text, false, new TextFieldConfig()),
            CancellationToken.None);

        model.Fields.Should().Contain(f => f.Name == "amount");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Model_not_found_throws_validation_exception()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        var act = async () => await CreateHandler().Handle(
            new AddFieldCommand(Guid.NewGuid(), OrgId, "amount", "Amount", FieldType.Text, false, new TextFieldConfig()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*not found*");
    }
}
