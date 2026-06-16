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
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private readonly IFormRepository _repo = Substitute.For<IFormRepository>();
    private readonly IFormModelReferenceSync _formModelReferenceSync = Substitute.For<IFormModelReferenceSync>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly RemoveFieldFromFormHandler _handler;

    public RemoveFieldFromFormHandlerTests()
        => _handler = new RemoveFieldFromFormHandler(_repo, _formModelReferenceSync, _uow);

    [Fact]
    public async Task Handle_WhenFieldExists_RemovesAndSaves()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, WorkspaceId, "user");
        FormField field = form.AddField("name", "Name", FormFieldType.Text, true, null);
        _repo.GetByIdAsync(form.Id, WorkspaceId, Arg.Any<CancellationToken>()).Returns(form);

        Result result = await _handler.Handle(
            new RemoveFieldFromFormCommand(form.Id, WorkspaceId, field.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        form.Fields.Should().BeEmpty();
        await _formModelReferenceSync.Received(1)
            .SyncRelationPickerReferencesAsync(form, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenFormNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), WorkspaceId, Arg.Any<CancellationToken>())
            .Returns((FormDefinition?)null);

        Result result = await _handler.Handle(
            new RemoveFieldFromFormCommand(Guid.NewGuid(), WorkspaceId, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenFormBelongsToAnotherWorkspace_ReturnsNotFound()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, WorkspaceId, "user");
        FormField field = form.AddField("name", "Name", FormFieldType.Text, true, null);
        _repo.GetByIdAsync(form.Id, WorkspaceId, Arg.Any<CancellationToken>()).Returns(form);

        Guid otherWorkspaceId = Guid.NewGuid();
        Result result = await _handler.Handle(
            new RemoveFieldFromFormCommand(form.Id, otherWorkspaceId, field.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _repo.Received(1).GetByIdAsync(form.Id, otherWorkspaceId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenFieldNotFound_ReturnsBusinessRule()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, WorkspaceId, "user");
        _repo.GetByIdAsync(form.Id, WorkspaceId, Arg.Any<CancellationToken>()).Returns(form);

        Result result = await _handler.Handle(
            new RemoveFieldFromFormCommand(form.Id, WorkspaceId, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }
}
