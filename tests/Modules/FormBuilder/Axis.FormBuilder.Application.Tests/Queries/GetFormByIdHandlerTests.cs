using Axis.FormBuilder.Application.Queries.GetFormById;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Queries;

public class GetFormByIdHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IFormRepository _repo = Substitute.For<IFormRepository>();
    private readonly GetFormByIdHandler _handler;

    public GetFormByIdHandlerTests() => _handler = new GetFormByIdHandler(_repo);

    [Fact]
    public async Task Handle_WhenFormExists_ReturnsDetailDto()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", "desc", OrgId, "user");
        form.AddField("first_name", "First Name", FormFieldType.Text, true, null);
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);

        FormDetailDto? dto = await _handler.Handle(
            new GetFormByIdQuery(form.Id, OrgId), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(form.Id);
        dto.Name.Should().Be("Employee Intake");
        dto.Description.Should().Be("desc");
        dto.Fields.Should().HaveCount(1);
        dto.Fields[0].Key.Should().Be("first_name");
        dto.Fields[0].Label.Should().Be("First Name");
        dto.Fields[0].Type.Should().Be(FormFieldType.Text);
        dto.Fields[0].Required.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenFormNotFound_ReturnsNull()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), OrgId, Arg.Any<CancellationToken>())
            .Returns((FormDefinition?)null);

        FormDetailDto? dto = await _handler.Handle(
            new GetFormByIdQuery(Guid.NewGuid(), OrgId), CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenFormHasNoFields_ReturnsEmptyFieldList()
    {
        FormDefinition form = FormDefinition.Create("Empty Form", null, OrgId, "user");
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);

        FormDetailDto? dto = await _handler.Handle(
            new GetFormByIdQuery(form.Id, OrgId), CancellationToken.None);

        dto!.Fields.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenFormBelongsToAnotherOrg_ReturnsNull()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, OrgId, "user");
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);

        Guid otherOrgId = Guid.NewGuid();
        FormDetailDto? dto = await _handler.Handle(
            new GetFormByIdQuery(form.Id, otherOrgId), CancellationToken.None);

        dto.Should().BeNull();
    }
}
