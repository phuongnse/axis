using Axis.Identity.Application.Commands.InviteUser;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class InviteUserHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IInvitationRepository _invitationRepo = Substitute.For<IInvitationRepository>();
    private readonly IOrganizationRepository _orgRepo = Substitute.For<IOrganizationRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid InvitedById = Guid.NewGuid();

    private InviteUserHandler CreateHandler() =>
        new(_userRepo, _roleRepo, _invitationRepo, _orgRepo, _emailSender, _uow);

    private InviteUserCommand ValidCommand() =>
        new(OrgId, "invited@example.com", RoleId, InvitedById);

    [Fact]
    public async Task Happy_path_creates_invitation_and_sends_email()
    {
        var email = Email.Create("invited@example.com").Value;
        _userRepo.GetByEmailAsync(Arg.Any<Email>(), OrgId).ReturnsNull();
        _invitationRepo.GetPendingByEmailAsync(Arg.Any<Email>(), OrgId).ReturnsNull();
        var role = Role.Create("Editor", null, OrgId, ["workflow:definition:read"]);
        _roleRepo.GetByIdAsync(RoleId, OrgId).Returns(role);
        var org = Organization.Create("Acme", OrganizationSlug.Create("acme").Value,
            Email.Create("admin@acme.com").Value);
        _orgRepo.GetByIdAsync(OrgId).Returns(org);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await _invitationRepo.Received(1).AddAsync(Arg.Any<Invitation>(), Arg.Any<CancellationToken>());
        await _emailSender.Received(1).SendInvitationEmailAsync(
            "invited@example.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Inviting_existing_member_throws_validation_exception()
    {
        var existingUser = User.Create("Bob", "Jones", Email.Create("invited@example.com").Value, OrgId);
        _userRepo.GetByEmailAsync(Arg.Any<Email>(), OrgId).Returns(existingUser);

        var act = async () => await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*already a member*");
    }

    [Fact]
    public async Task Inviting_with_pending_invitation_throws_validation_exception()
    {
        _userRepo.GetByEmailAsync(Arg.Any<Email>(), OrgId).ReturnsNull();
        var existing = Invitation.Create(
            Email.Create("invited@example.com").Value, OrgId, RoleId, InvitedById);
        _invitationRepo.GetPendingByEmailAsync(Arg.Any<Email>(), OrgId).Returns(existing);

        var act = async () => await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*already been sent*");
    }

    [Fact]
    public async Task Inviting_self_throws_validation_exception()
    {
        var selfCommand = new InviteUserCommand(OrgId, "invited@example.com", RoleId,
            InvitedById: InvitedById);
        var inviter = User.Create("Alice", "Smith",
            Email.Create("invited@example.com").Value, OrgId);
        // Same user Id
        _userRepo.GetByEmailAsync(Arg.Any<Email>(), OrgId).Returns(inviter);

        // The handler should detect self-invite via the existing member check or a dedicated check
        var act = async () => await CreateHandler().Handle(selfCommand, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Role_not_found_throws_validation_exception()
    {
        _userRepo.GetByEmailAsync(Arg.Any<Email>(), OrgId).ReturnsNull();
        _invitationRepo.GetPendingByEmailAsync(Arg.Any<Email>(), OrgId).ReturnsNull();
        _roleRepo.GetByIdAsync(RoleId, OrgId).ReturnsNull();

        var act = async () => await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*role*not found*");
    }
}
