using Axis.Identity.Application.Commands.UpdateRole;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class UpdateRoleHandlerTests
{
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();

    private UpdateRoleHandler CreateHandler() => new(_roleRepo, _uow);

    private static UpdateRoleCommand ValidCommand() => new(
        RoleId, OrgId,
        Name: "Senior Manager",
        Description: "Updated",
        Permissions: ["workflow:definition:read", "workflow:definition:write"]);

    [Fact]
    public async Task Happy_path_updates_role()
    {
        var role = Role.Create("Manager", null, OrgId, ["workflow:definition:read"]);
        _roleRepo.GetByIdAsync(RoleId, OrgId).Returns(role);
        _roleRepo.NameExistsAsync("Senior Manager", OrgId, role.Id).Returns(false);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        role.Name.Should().Be("Senior Manager");
        role.Description.Should().Be("Updated");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task System_role_cannot_be_updated()
    {
        var systemRole = Role.CreateSystem("Admin", OrgId, ["users:read"]);
        _roleRepo.GetByIdAsync(RoleId, OrgId).Returns(systemRole);

        var act = async () => await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        // US-023: system roles cannot be edited
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*system role*");
    }

    [Fact]
    public async Task Duplicate_name_throws_validation_exception()
    {
        var role = Role.Create("Manager", null, OrgId, ["workflow:definition:read"]);
        _roleRepo.GetByIdAsync(RoleId, OrgId).Returns(role);
        _roleRepo.NameExistsAsync("Senior Manager", OrgId, role.Id).Returns(true);

        var act = async () => await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task Role_not_found_throws_validation_exception()
    {
        _roleRepo.GetByIdAsync(RoleId, OrgId).ReturnsNull();

        var act = async () => await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*not found*");
    }
}
