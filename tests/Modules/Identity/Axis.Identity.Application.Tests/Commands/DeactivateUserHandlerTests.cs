using Axis.Identity.Application.Commands.DeactivateUser;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class DeactivateUserHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid AdminRoleId = Guid.NewGuid();
    private static readonly Guid RequesterId = Guid.NewGuid();

    private DeactivateUserHandler CreateHandler() =>
        new(_userRepo, _uow);

    private static User MakeUser(string email = "user@acme.com") =>
        User.Create("Test", "User", Email.Create(email).Value, OrgId);

    [Fact]
    public async Task Happy_path_deactivates_user()
    {
        var target = MakeUser();
        var requester = MakeUser("admin@acme.com");
        _userRepo.GetByIdAsync(target.Id, OrgId).Returns(target);
        _userRepo.GetByIdAsync(RequesterId, OrgId).Returns(requester);
        _userRepo.CountAdminsAsync(OrgId, AdminRoleId).Returns(2);
        var adminRole = Role.CreateSystem("Admin", OrgId, ["users:deactivate"]);
        _roleRepo.GetByIdAsync(AdminRoleId, OrgId).Returns(adminRole);

        await CreateHandler().Handle(
            new DeactivateUserCommand(target.Id, OrgId, RequesterId, AdminRoleId),
            CancellationToken.None);

        target.Status.Should().Be(UserStatus.Inactive);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Admin_cannot_deactivate_themselves()
    {
        var user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user); // requester = same user

        var act = async () => await CreateHandler().Handle(
            new DeactivateUserCommand(user.Id, OrgId, user.Id, AdminRoleId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*cannot deactivate yourself*");
    }

    [Fact]
    public async Task Cannot_deactivate_last_admin()
    {
        var target = MakeUser();
        target.AssignRole(AdminRoleId); // target IS the last admin
        var requester = MakeUser("admin@acme.com");
        _userRepo.GetByIdAsync(target.Id, OrgId).Returns(target);
        _userRepo.GetByIdAsync(RequesterId, OrgId).Returns(requester);
        _userRepo.CountAdminsAsync(OrgId, AdminRoleId).Returns(1); // only 1 admin left

        var act = async () => await CreateHandler().Handle(
            new DeactivateUserCommand(target.Id, OrgId, RequesterId, AdminRoleId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*last admin*");
    }

    [Fact]
    public async Task User_not_found_throws_validation_exception()
    {
        _userRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        var act = async () => await CreateHandler().Handle(
            new DeactivateUserCommand(Guid.NewGuid(), OrgId, RequesterId, AdminRoleId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*not found*");
    }
}
