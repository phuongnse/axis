using Axis.FormBuilder.Application.Commands.AddFieldToForm;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Commands;

public class AddFieldToFormHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IFormRepository _repo = Substitute.For<IFormRepository>();
    private readonly IFormModelReferenceSync _formModelReferenceSync = Substitute.For<IFormModelReferenceSync>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly AddFieldToFormHandler _handler;

    public AddFieldToFormHandlerTests()
        => _handler = new AddFieldToFormHandler(_repo, _formModelReferenceSync, _uow);

    [Fact]
    public async Task Handle_WhenFormExists_AddsFieldAndReturnsFieldId()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, OrgId, "user");
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);

        Result<Guid> result = await _handler.Handle(
            new AddFieldToFormCommand(form.Id, OrgId, "full_name", "Full Name", FormFieldType.Text, true, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        form.Fields.Should().ContainSingle(f => f.Key == "full_name" && f.Label == "Full Name");
        await _formModelReferenceSync.Received(1)
            .SyncRelationPickerReferencesAsync(form, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenFormNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), OrgId, Arg.Any<CancellationToken>())
            .Returns((FormDefinition?)null);

        Result<Guid> result = await _handler.Handle(
            new AddFieldToFormCommand(Guid.NewGuid(), OrgId, "key", "Label", FormFieldType.Text, false, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenDuplicateFieldKey_ReturnsBusinessRule()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, OrgId, "user");
        form.AddField("email", "Email", FormFieldType.Text, true, null);
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);

        Result<Guid> result = await _handler.Handle(
            new AddFieldToFormCommand(form.Id, OrgId, "email", "Email Again", FormFieldType.Text, false, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }

    [Fact]
    public async Task Handle_WhenFormBelongsToAnotherOrg_ReturnsNotFound()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, OrgId, "user");
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);

        Guid otherOrgId = Guid.NewGuid();
        Result<Guid> result = await _handler.Handle(
            new AddFieldToFormCommand(form.Id, otherOrgId, "key", "Label", FormFieldType.Text, false, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _repo.Received(1).GetByIdAsync(form.Id, otherOrgId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenInvalidFieldKey_ReturnsBusinessRule()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, OrgId, "user");
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);

        Result<Guid> result = await _handler.Handle(
            new AddFieldToFormCommand(form.Id, OrgId, "123invalid", "Label", FormFieldType.Text, false, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }
}
