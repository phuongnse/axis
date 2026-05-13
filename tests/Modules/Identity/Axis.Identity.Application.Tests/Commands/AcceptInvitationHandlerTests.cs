using Axis.Identity.Application.Commands.AcceptInvitation;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class AcceptInvitationHandlerTests
{
    private readonly IInvitationRepository _invitationRepo = Substitute.For<IInvitationRepository>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid InvitedById = Guid.NewGuid();
    private static readonly Email InvitedEmail = Email.Create("new@acme.com").Value;

    private AcceptInvitationHandler CreateHandler() =>
        new(_invitationRepo, _userRepo, _roleRepo, _hasher, _uow);

    private AcceptInvitationCommand ValidCommand(string token = "valid-token") =>
        new(token, "Bob", "Jones", "NewPass1");

    private Invitation MakePendingInvitation() =>
        Invitation.Create(InvitedEmail, OrgId, RoleId, InvitedById);

    [Fact]
    public async Task Happy_path_creates_user_accepts_invitation_and_assigns_role()
    {
        Invitation invitation = MakePendingInvitation();
        _invitationRepo.GetByTokenAsync("valid-token").Returns(invitation);
        _userRepo.EmailExistsPlatformWideAsync(InvitedEmail).Returns(false);
        _hasher.Hash("NewPass1").Returns("hashed");
        _roleRepo.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), OrgId).Returns([]);

        Result<AcceptInvitationResult> result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().NotBeEmpty();
        result.Value.OrganizationId.Should().Be(OrgId);
        result.Value.Email.Should().Be("new@acme.com");
        await _userRepo.Received(1).AddAsync(
            Arg.Is<User>(u =>
                u.FirstName == "Bob" &&
                u.LastName == "Jones" &&
                u.OrganizationId == OrgId &&
                u.RoleIds.Contains(RoleId) &&
                u.PasswordHash == "hashed"),
            Arg.Any<CancellationToken>());

        invitation.Status.Should().Be(InvitationStatus.Accepted);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Email_already_exists_platform_wide_returns_conflict()
    {
        Invitation invitation = MakePendingInvitation();
        _invitationRepo.GetByTokenAsync("valid-token").Returns(invitation);
        _userRepo.EmailExistsPlatformWideAsync(InvitedEmail).Returns(true);

        Result<AcceptInvitationResult> result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task Invalid_token_returns_not_found()
    {
        _invitationRepo.GetByTokenAsync(Arg.Any<string>()).ReturnsNull();

        Result<AcceptInvitationResult> result = await CreateHandler().Handle(ValidCommand("bad-token"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("Invalid");
    }

    [Fact]
    public async Task Expired_invitation_returns_business_rule_failure()
    {
        Invitation expired = InvitationTestHelper.CreateExpired(InvitedEmail, OrgId, RoleId, InvitedById);
        _invitationRepo.GetByTokenAsync("valid-token").Returns(expired);

        Result<AcceptInvitationResult> result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("expired");
    }

    [Fact]
    public async Task Already_accepted_invitation_returns_business_rule_failure()
    {
        Invitation invitation = MakePendingInvitation();
        invitation.Accept();
        _invitationRepo.GetByTokenAsync("valid-token").Returns(invitation);

        Result<AcceptInvitationResult> result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("already been used");
    }

    [Fact]
    public async Task Cancelled_invitation_returns_business_rule_failure()
    {
        Invitation invitation = MakePendingInvitation();
        invitation.Cancel();
        _invitationRepo.GetByTokenAsync("valid-token").Returns(invitation);

        Result<AcceptInvitationResult> result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("cancelled");
    }
}
