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
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly IFormRepository _repo = Substitute.For<IFormRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly UpdateFormHandler _handler;

    public UpdateFormHandlerTests() => _handler = new UpdateFormHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenFormExistsAndNameUnique_UpdatesAndSaves()
    {
        FormDefinition form = FormDefinition.Create("Old Name", null, TenantId, "user");
        _repo.GetByIdAsync(form.Id, TenantId, Arg.Any<CancellationToken>()).Returns(form);
        _repo.NameExistsAsync("New Name", TenantId, form.Id, Arg.Any<CancellationToken>()).Returns(false);

        Result result = await _handler.Handle(
            new UpdateFormCommand(form.Id, TenantId, "New Name", "Updated desc"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        form.Name.Should().Be("New Name");
        form.Description.Should().Be("Updated desc");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenFormNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), TenantId, Arg.Any<CancellationToken>())
            .Returns((FormDefinition?)null);

        Result result = await _handler.Handle(
            new UpdateFormCommand(Guid.NewGuid(), TenantId, "Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenFormBelongsToAnotherTenant_ReturnsNotFound()
    {
        FormDefinition form = FormDefinition.Create("Old Name", null, TenantId, "user");
        _repo.GetByIdAsync(form.Id, TenantId, Arg.Any<CancellationToken>()).Returns(form);

        Guid otherTenantId = Guid.NewGuid();
        Result result = await _handler.Handle(
            new UpdateFormCommand(form.Id, otherTenantId, "New Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ReturnsConflict()
    {
        FormDefinition form = FormDefinition.Create("Old Name", null, TenantId, "user");
        _repo.GetByIdAsync(form.Id, TenantId, Arg.Any<CancellationToken>()).Returns(form);
        _repo.NameExistsAsync("Taken Name", TenantId, form.Id, Arg.Any<CancellationToken>()).Returns(true);

        Result result = await _handler.Handle(
            new UpdateFormCommand(form.Id, TenantId, "Taken Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task Handle_WhenSaveThrowsUniqueConstraint_ReturnsConflict()
    {
        FormDefinition form = FormDefinition.Create("Old Name", null, TenantId, "user");
        _repo.GetByIdAsync(form.Id, TenantId, Arg.Any<CancellationToken>()).Returns(form);
        _repo.NameExistsAsync("Concurrent Name", TenantId, form.Id, Arg.Any<CancellationToken>()).Returns(false);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new UniqueConstraintException("unique violation"));

        Result result = await _handler.Handle(
            new UpdateFormCommand(form.Id, TenantId, "Concurrent Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }
}
