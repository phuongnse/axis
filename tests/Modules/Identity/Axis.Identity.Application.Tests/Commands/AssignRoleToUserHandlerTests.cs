using Axis.Identity.Application.Commands.AssignRoleToUser;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class AssignRoleToUserHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid AdminRoleId = Guid.NewGuid();
    private static readonly Guid EditorRoleId = Guid.NewGuid();

    private AssignRoleToUserHandler CreateHandler() => new(_userRepo, _roleRepo, _uow);

    private static User MakeUser(string email = "user@acme.com")
    {
        var u = User.Create("Test", "User", Email.Create(email).Value, OrgId);
        u.AssignRole(EditorRoleId);
        return u;
    }

    [Fact]
    public async Task Assign_role_adds_to_user()
    {
        var user = MakeUser();
        var newRole = Role.Create("Manager", null, OrgId, ["workflow:definition:read"]);
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _roleRepo.GetByIdAsync(newRole.Id, OrgId).Returns(newRole);

        await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, OrgId, newRole.Id, Action: RoleAction.Assign),
            CancellationToken.None);

        user.RoleIds.Should().Contain(newRole.Id);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_role_removes_from_user()
    {
        var user = MakeUser();
        var editorRole = Role.Create("Editor", null, OrgId, ["workflow:definition:read"]);
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _roleRepo.GetByIdAsync(EditorRoleId, OrgId).Returns(editorRole);
        user.AssignRole(AdminRoleId); // give another role so we can remove editor

        await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, OrgId, EditorRoleId, Action: RoleAction.Remove),
            CancellationToken.None);

        user.RoleIds.Should().NotContain(EditorRoleId);
    }

    [Fact]
    public async Task Removing_last_role_throws_validation_exception()
    {
        // User has only EditorRoleId
        var user = MakeUser();
        var editorRole = Role.Create("Editor", null, OrgId, ["workflow:definition:read"]);
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _roleRepo.GetByIdAsync(EditorRoleId, OrgId).Returns(editorRole);

        var act = async () => await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, OrgId, EditorRoleId, Action: RoleAction.Remove),
            CancellationToken.None);

        // US-024: a user must always have at least one role
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*at least one role*");
    }

    [Fact]
    public async Task Removing_admin_role_from_last_admin_throws()
    {
        var user = MakeUser();
        user.AssignRole(AdminRoleId);
        var adminRole = Role.CreateSystem("Admin", OrgId, ["users:read"]);
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _roleRepo.GetByIdAsync(AdminRoleId, OrgId).Returns(adminRole);
        _userRepo.CountAdminsAsync(OrgId, AdminRoleId).Returns(1); // last admin

        var act = async () => await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, OrgId, AdminRoleId, Action: RoleAction.Remove),
            CancellationToken.None);

        // US-024: last admin guard
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*last admin*");
    }

    [Fact]
    public async Task Role_not_found_in_org_throws_validation_exception()
    {
        var user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _roleRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        var act = async () => await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, OrgId, Guid.NewGuid(), Action: RoleAction.Assign),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*role*not found*");
    }
}
