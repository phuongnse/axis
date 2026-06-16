using Axis.FormBuilder.Application.Commands.ReorderFormFields;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Entities;
using Axis.FormBuilder.Domain.Enums;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Commands;

public class ReorderFormFieldsHandlerTests
{
    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private readonly IFormRepository _repo = Substitute.For<IFormRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ReorderFormFieldsHandler _handler;

    public ReorderFormFieldsHandlerTests() => _handler = new ReorderFormFieldsHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenIdsMatchAllFields_ReordersAndSaves()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, TeamAccountId, "user");
        FormField f1 = form.AddField("field_a", "Field A", FormFieldType.Text, false, null);
        FormField f2 = form.AddField("field_b", "Field B", FormFieldType.Text, false, null);
        FormField f3 = form.AddField("field_c", "Field C", FormFieldType.Text, false, null);
        _repo.GetByIdAsync(form.Id, TeamAccountId, Arg.Any<CancellationToken>()).Returns(form);

        Result result = await _handler.Handle(
            new ReorderFormFieldsCommand(form.Id, TeamAccountId, [f3.Id, f1.Id, f2.Id]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        form.Fields.Single(f => f.Id == f3.Id).DisplayOrder.Should().Be(0);
        form.Fields.Single(f => f.Id == f1.Id).DisplayOrder.Should().Be(1);
        form.Fields.Single(f => f.Id == f2.Id).DisplayOrder.Should().Be(2);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenFormNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), TeamAccountId, Arg.Any<CancellationToken>())
            .Returns((FormDefinition?)null);

        Result result = await _handler.Handle(
            new ReorderFormFieldsCommand(Guid.NewGuid(), TeamAccountId, [Guid.NewGuid()]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenFormBelongsToAnotherTeamAccount_ReturnsNotFound()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, TeamAccountId, "user");
        FormField f1 = form.AddField("field_a", "Field A", FormFieldType.Text, false, null);
        _repo.GetByIdAsync(form.Id, TeamAccountId, Arg.Any<CancellationToken>()).Returns(form);

        Guid otherTeamAccountId = Guid.NewGuid();
        Result result = await _handler.Handle(
            new ReorderFormFieldsCommand(form.Id, otherTeamAccountId, [f1.Id]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _repo.Received(1).GetByIdAsync(form.Id, otherTeamAccountId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenIdsMismatchFields_ReturnsBusinessRule()
    {
        FormDefinition form = FormDefinition.Create("My Form", null, TeamAccountId, "user");
        form.AddField("field_a", "Field A", FormFieldType.Text, false, null);
        _repo.GetByIdAsync(form.Id, TeamAccountId, Arg.Any<CancellationToken>()).Returns(form);

        Result result = await _handler.Handle(
            new ReorderFormFieldsCommand(form.Id, TeamAccountId, [Guid.NewGuid(), Guid.NewGuid()]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }
}
