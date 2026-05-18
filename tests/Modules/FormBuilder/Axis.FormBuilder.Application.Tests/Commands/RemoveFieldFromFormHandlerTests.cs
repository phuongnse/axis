using Axis.FormBuilder.Application.Commands.RemoveFieldFromForm;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Entities;
using Axis.FormBuilder.Domain.Enums;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Commands;

public class RemoveFieldFromFormHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IFormRepository _repo = Substitute.For<IFormRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly RemoveFieldFromFormHandler _handler;

    public RemoveFieldFromFormHandlerTests() => _handler = new RemoveFieldFromFormHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenFieldExists_RemovesAndSaves()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, OrgId, "user");
        FormField field = form.AddField("name", "Name", FormFieldType.Text, true, null);
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);

        Result result = await _handler.Handle(
            new RemoveFieldFromFormCommand(form.Id, OrgId, field.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        form.Fields.Should().BeEmpty();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenFormNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), OrgId, Arg.Any<CancellationToken>())
            .Returns((FormDefinition?)null);

        Result result = await _handler.Handle(
            new RemoveFieldFromFormCommand(Guid.NewGuid(), OrgId, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenFormBelongsToAnotherOrg_ReturnsNotFound()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, OrgId, "user");
        FormField field = form.AddField("name", "Name", FormFieldType.Text, true, null);
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);

        Guid otherOrgId = Guid.NewGuid();
        Result result = await _handler.Handle(
            new RemoveFieldFromFormCommand(form.Id, otherOrgId, field.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenFieldNotFound_ReturnsBusinessRule()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, OrgId, "user");
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);

        Result result = await _handler.Handle(
            new RemoveFieldFromFormCommand(form.Id, OrgId, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }
}
