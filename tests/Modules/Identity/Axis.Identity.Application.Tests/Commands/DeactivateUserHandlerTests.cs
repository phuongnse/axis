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
    private readonly ISessionStore _sessionStore = Substitute.For<ISessionStore>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid RequesterId = Guid.NewGuid();
    private readonly Role _adminRole = Role.CreateSystem("Admin", OrgId, ["users:deactivate"]);

    public DeactivateUserHandlerTests()
    {
        _roleRepo.GetByNameAsync("Admin", OrgId, Arg.Any<CancellationToken>()).Returns(_adminRole);
    }

    private DeactivateUserHandler CreateHandler() =>
        new(_userRepo, _roleRepo, _sessionStore, _uow);

    private static User MakeUser(string email = "user@acme.com") =>
        User.Create("Test", "User", Email.Create(email).Value, OrgId);

    [Fact]
    public async Task DeactivateUser_WhenRequestIsValid_DeactivatesUserAndRevokesSessions()
    {
        User target = MakeUser();
        _userRepo.GetByIdAsync(target.Id, OrgId).Returns(target);
        _userRepo.CountAdminsAsync(OrgId, _adminRole.Id).Returns(2);
        target.AssignRole(_adminRole.Id);

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(target.Id, OrgId, RequesterId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        target.Status.Should().Be(UserStatus.Inactive);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _sessionStore.Received(1).RevokeAllAsync(target.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateUser_WhenSelfDeactivation_ReturnsBusinessRuleFailure()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(user.Id, OrgId, user.Id),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("cannot deactivate yourself");
    }

    [Fact]
    public async Task DeactivateUser_WhenLastAdmin_ReturnsBusinessRuleFailure()
    {
        User target = MakeUser();
        target.AssignRole(_adminRole.Id);
        _userRepo.GetByIdAsync(target.Id, OrgId).Returns(target);
        _userRepo.CountAdminsAsync(OrgId, _adminRole.Id).Returns(1);

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(target.Id, OrgId, RequesterId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("last admin");
    }

    [Fact]
    public async Task DeactivateUser_WhenUserNotFound_ReturnsNotFound()
    {
        _userRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(Guid.NewGuid(), OrgId, RequesterId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task DeactivateUser_WhenAdminRoleMissing_ReturnsNotFound()
    {
        User target = MakeUser();
        _userRepo.GetByIdAsync(target.Id, OrgId).Returns(target);
        _roleRepo.GetByNameAsync("Admin", OrgId, Arg.Any<CancellationToken>()).Returns((Role?)null);

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(target.Id, OrgId, RequesterId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("Admin role not found");
    }

    [Fact]
    public async Task DeactivateUser_WhenUserBelongsToAnotherOrg_ReturnsNotFound()
    {
        User target = MakeUser();
        _userRepo.GetByIdAsync(target.Id, OrgId).Returns(target);

        Guid otherOrgId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(target.Id, otherOrgId, RequesterId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
