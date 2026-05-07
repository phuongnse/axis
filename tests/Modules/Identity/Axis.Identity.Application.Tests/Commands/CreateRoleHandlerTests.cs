using Axis.Identity.Application.Commands.CreateRole;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public class CreateRoleHandlerTests
{
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private CreateRoleHandler CreateHandler() => new(_roleRepo, _uow);

    private static CreateRoleCommand ValidCommand() => new(
        OrgId,
        Name: "Manager",
        Description: "Can manage workflows",
        Permissions: ["workflow:definition:read", "workflow:definition:write"]);

    [Fact]
    public async Task Happy_path_creates_and_persists_role()
    {
        _roleRepo.NameExistsAsync("Manager", OrgId).Returns(false);

        var roleId = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        roleId.Should().NotBeEmpty();
        await _roleRepo.Received(1).AddAsync(
            Arg.Is<Role>(r => r.Name == "Manager" && r.OrganizationId == OrgId),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Duplicate_name_throws_validation_exception()
    {
        _roleRepo.NameExistsAsync("Manager", OrgId).Returns(true);

        var act = async () => await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task Empty_permissions_throws_validation_exception()
    {
        var command = ValidCommand() with { Permissions = [] };
        _roleRepo.NameExistsAsync(Arg.Any<string>(), OrgId).Returns(false);

        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*at least one permission*");
    }
}
