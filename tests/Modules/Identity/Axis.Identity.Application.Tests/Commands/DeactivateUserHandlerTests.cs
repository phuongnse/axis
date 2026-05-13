using Axis.Identity.Application.Commands.DeactivateUser;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
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
        User target = MakeUser();
        User requester = MakeUser("admin@acme.com");
        _userRepo.GetByIdAsync(target.Id, OrgId).Returns(target);
        _userRepo.GetByIdAsync(RequesterId, OrgId).Returns(requester);
        _userRepo.CountAdminsAsync(OrgId, AdminRoleId).Returns(2);
        Role adminRole = Role.CreateSystem("Admin", OrgId, ["users:deactivate"]);
        _roleRepo.GetByIdAsync(AdminRoleId, OrgId).Returns(adminRole);

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(target.Id, OrgId, RequesterId, AdminRoleId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        target.Status.Should().Be(UserStatus.Inactive);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Admin_cannot_deactivate_themselves_returns_business_rule_failure()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(user.Id, OrgId, user.Id, AdminRoleId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("cannot deactivate yourself");
    }

    [Fact]
    public async Task Cannot_deactivate_last_admin_returns_business_rule_failure()
    {
        User target = MakeUser();
        target.AssignRole(AdminRoleId);
        User requester = MakeUser("admin@acme.com");
        _userRepo.GetByIdAsync(target.Id, OrgId).Returns(target);
        _userRepo.GetByIdAsync(RequesterId, OrgId).Returns(requester);
        _userRepo.CountAdminsAsync(OrgId, AdminRoleId).Returns(1);

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(target.Id, OrgId, RequesterId, AdminRoleId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("last admin");
    }

    [Fact]
    public async Task User_not_found_returns_not_found()
    {
        _userRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(Guid.NewGuid(), OrgId, RequesterId, AdminRoleId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }
}
