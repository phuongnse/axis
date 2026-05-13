using Axis.DataModeling.Application.Commands.ReorderFields;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Entities;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class ReorderFieldsHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private ReorderFieldsHandler CreateHandler() => new(_modelRepo, _uow);

    [Fact]
    public async Task Happy_path_reorders_custom_fields_and_saves()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, OrgId, UserId);
        FieldDefinition f1 = model.AddField("alpha", "Alpha", FieldType.Text, false, new TextFieldConfig());
        FieldDefinition f2 = model.AddField("beta", "Beta", FieldType.Text, false, new TextFieldConfig());
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        // Reverse order: beta first, alpha second
        Result result = await CreateHandler().Handle(
            new ReorderFieldsCommand(model.Id, OrgId, [f2.Id, f1.Id]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        List<FieldDefinition> customFields = model.Fields.Where(f => !f.IsSystem).OrderBy(f => f.DisplayOrder).ToList();
        customFields[0].Id.Should().Be(f2.Id);
        customFields[1].Id.Should().Be(f1.Id);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mismatched_ids_return_business_rule_failure()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, OrgId, UserId);
        model.AddField("alpha", "Alpha", FieldType.Text, false, new TextFieldConfig());
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        Result result = await CreateHandler().Handle(
            new ReorderFieldsCommand(model.Id, OrgId, [Guid.NewGuid()]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }
}
