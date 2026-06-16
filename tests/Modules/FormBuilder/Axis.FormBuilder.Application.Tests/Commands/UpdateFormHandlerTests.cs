using Axis.FormBuilder.Application.Commands.UpdateForm;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Commands;

public class UpdateFormHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IFormRepository _repo = Substitute.For<IFormRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly UpdateFormHandler _handler;

    public UpdateFormHandlerTests() => _handler = new UpdateFormHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenFormExistsAndNameUnique_UpdatesAndSaves()
    {
        FormDefinition form = FormDefinition.Create("Old Name", null, OrgId, "user");
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);
        _repo.NameExistsAsync("New Name", OrgId, form.Id, Arg.Any<CancellationToken>()).Returns(false);

        Result result = await _handler.Handle(
            new UpdateFormCommand(form.Id, OrgId, "New Name", "Updated desc"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        form.Name.Should().Be("New Name");
        form.Description.Should().Be("Updated desc");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenFormNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), OrgId, Arg.Any<CancellationToken>())
            .Returns((FormDefinition?)null);

        Result result = await _handler.Handle(
            new UpdateFormCommand(Guid.NewGuid(), OrgId, "Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenFormBelongsToAnotherOrg_ReturnsNotFound()
    {
        FormDefinition form = FormDefinition.Create("Old Name", null, OrgId, "user");
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);

        Guid otherOrgId = Guid.NewGuid();
        Result result = await _handler.Handle(
            new UpdateFormCommand(form.Id, otherOrgId, "New Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ReturnsConflict()
    {
        FormDefinition form = FormDefinition.Create("Old Name", null, OrgId, "user");
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);
        _repo.NameExistsAsync("Taken Name", OrgId, form.Id, Arg.Any<CancellationToken>()).Returns(true);

        Result result = await _handler.Handle(
            new UpdateFormCommand(form.Id, OrgId, "Taken Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task Handle_WhenSaveThrowsUniqueConstraint_ReturnsConflict()
    {
        FormDefinition form = FormDefinition.Create("Old Name", null, OrgId, "user");
        _repo.GetByIdAsync(form.Id, OrgId, Arg.Any<CancellationToken>()).Returns(form);
        _repo.NameExistsAsync("Concurrent Name", OrgId, form.Id, Arg.Any<CancellationToken>()).Returns(false);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new UniqueConstraintException("unique violation"));

        Result result = await _handler.Handle(
            new UpdateFormCommand(form.Id, OrgId, "Concurrent Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }
}
