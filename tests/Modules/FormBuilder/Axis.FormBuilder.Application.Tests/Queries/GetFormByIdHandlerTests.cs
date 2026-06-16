using Axis.FormBuilder.Application.Queries.GetFormById;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Entities;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Queries;

public class GetFormByIdHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly IFormRepository _repo = Substitute.For<IFormRepository>();
    private readonly IFormModelReferenceRepository _formModelReferenceRepo = Substitute.For<IFormModelReferenceRepository>();
    private readonly GetFormByIdHandler _handler;

    public GetFormByIdHandlerTests()
    {
        _handler = new GetFormByIdHandler(_repo, _formModelReferenceRepo);
        _formModelReferenceRepo.GetBrokenFieldIdsForFormAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid>());
    }

    [Fact]
    public async Task Handle_WhenFormExists_ReturnsDetailDto()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", "desc", TenantId, "user");
        form.AddField("first_name", "First Name", FormFieldType.Text, true, null);
        _repo.GetByIdAsync(form.Id, TenantId, Arg.Any<CancellationToken>()).Returns(form);

        FormDetailDto? dto = await _handler.Handle(
            new GetFormByIdQuery(form.Id, TenantId), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(form.Id);
        dto.Name.Should().Be("Employee Intake");
        dto.Description.Should().Be("desc");
        dto.Fields.Should().HaveCount(1);
        dto.Fields[0].Key.Should().Be("first_name");
        dto.Fields[0].Label.Should().Be("First Name");
        dto.Fields[0].Type.Should().Be(FormFieldType.Text);
        dto.Fields[0].Required.Should().BeTrue();
        dto.Fields[0].IsBroken.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenFormNotFound_ReturnsNull()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), TenantId, Arg.Any<CancellationToken>())
            .Returns((FormDefinition?)null);

        FormDetailDto? dto = await _handler.Handle(
            new GetFormByIdQuery(Guid.NewGuid(), TenantId), CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenFormHasNoFields_ReturnsEmptyFieldList()
    {
        FormDefinition form = FormDefinition.Create("Empty Form", null, TenantId, "user");
        _repo.GetByIdAsync(form.Id, TenantId, Arg.Any<CancellationToken>()).Returns(form);

        FormDetailDto? dto = await _handler.Handle(
            new GetFormByIdQuery(form.Id, TenantId), CancellationToken.None);

        dto!.Fields.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenFormBelongsToAnotherTenant_ReturnsNull()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, TenantId, "user");
        _repo.GetByIdAsync(form.Id, TenantId, Arg.Any<CancellationToken>()).Returns(form);

        Guid otherTenantId = Guid.NewGuid();
        FormDetailDto? dto = await _handler.Handle(
            new GetFormByIdQuery(form.Id, otherTenantId), CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenFieldIsBroken_MarksDtoAsBroken()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, TenantId, "user");
        FormField field = form.AddField(
            "company",
            "Company",
            FormFieldType.RelationPicker,
            false,
            new RelationPickerFieldConfig(Guid.NewGuid()));
        _repo.GetByIdAsync(form.Id, TenantId, Arg.Any<CancellationToken>()).Returns(form);
        _formModelReferenceRepo.GetBrokenFieldIdsForFormAsync(form.Id, Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { field.Id });

        FormDetailDto? dto = await _handler.Handle(
            new GetFormByIdQuery(form.Id, TenantId), CancellationToken.None);

        dto!.Fields.Should().ContainSingle();
        dto.Fields[0].IsBroken.Should().BeTrue();
    }
}
