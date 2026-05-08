using Axis.Identity.Application.Commands.AcceptInvitation;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class AcceptInvitationHandlerTests
{
    private readonly IInvitationRepository _invitationRepo = Substitute.For<IInvitationRepository>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid InvitedById = Guid.NewGuid();
    private static readonly Email InvitedEmail = Email.Create("new@acme.com").Value;

    private AcceptInvitationHandler CreateHandler() =>
        new(_invitationRepo, _userRepo, _hasher, _uow);

    private AcceptInvitationCommand ValidCommand(string token = "valid-token") =>
        new(token, "Bob", "Jones", "NewPass1");

    private Invitation MakePendingInvitation() =>
        Invitation.Create(InvitedEmail, OrgId, RoleId, InvitedById);

    [Fact]
    public async Task Happy_path_creates_user_accepts_invitation_and_assigns_role()
    {
        var invitation = MakePendingInvitation();
        _invitationRepo.GetByTokenAsync("valid-token").Returns(invitation);
        _userRepo.EmailExistsPlatformWideAsync(InvitedEmail).Returns(false);
        _hasher.Hash("NewPass1").Returns("hashed");

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

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
    public async Task Invalid_token_throws_validation_exception()
    {
        _invitationRepo.GetByTokenAsync(Arg.Any<string>()).ReturnsNull();

        var act = async () => await CreateHandler().Handle(ValidCommand("bad-token"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Invalid*");
    }

    [Fact]
    public async Task Expired_invitation_throws_with_expiry_message()
    {
        // Use reflection/internal factory to create expired invitation
        var expired = InvitationTestHelper.CreateExpired(InvitedEmail, OrgId, RoleId, InvitedById);
        _invitationRepo.GetByTokenAsync("valid-token").Returns(expired);

        var act = async () => await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task Already_accepted_invitation_throws()
    {
        var invitation = MakePendingInvitation();
        invitation.Accept();
        _invitationRepo.GetByTokenAsync("valid-token").Returns(invitation);

        var act = async () => await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*already been used*");
    }

    [Fact]
    public async Task Cancelled_invitation_throws()
    {
        var invitation = MakePendingInvitation();
        invitation.Cancel();
        _invitationRepo.GetByTokenAsync("valid-token").Returns(invitation);

        var act = async () => await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*cancelled*");
    }
}
