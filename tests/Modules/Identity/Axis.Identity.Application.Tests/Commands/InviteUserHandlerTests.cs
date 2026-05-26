using Axis.Identity.Application.Commands.InviteUser;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
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
    private readonly IPlanLimitService _planLimitService = Substitute.For<IPlanLimitService>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid InvitedById = Guid.NewGuid();

    private InviteUserHandler CreateHandler()
    {
        _planLimitService.EnsureWithinLimitAsync(Arg.Any<Guid>(), Arg.Any<PlanLimitResourceType>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        return new(_planLimitService, _userRepo, _roleRepo, _invitationRepo, _orgRepo, _emailSender, _uow);
    }

    private InviteUserCommand ValidCommand() =>
        new(OrgId, "invited@example.com", RoleId, InvitedById);

    [Fact]
    public async Task InviteUser_WhenRequestIsValid_CreatesInvitationAndSendsEmail()
    {
        Email email = Email.Create("invited@example.com").Value;
        _userRepo.GetByEmailAsync(Arg.Any<Email>(), OrgId).ReturnsNull();
        _invitationRepo.GetPendingByEmailAsync(Arg.Any<Email>(), OrgId).ReturnsNull();
        Role role = Role.Create("Editor", null, OrgId, ["workflow:definition:read"]);
        _roleRepo.GetByIdAsync(RoleId, OrgId).Returns(role);
        Organization org = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value,
            Email.Create("admin@acme.com").Value,
            Domain.Subscriptions.WellKnownSubscriptionPlans.FreeId);
        _orgRepo.GetByIdAsync(OrgId).Returns(org);

        Result result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _invitationRepo.Received(1).AddAsync(Arg.Any<Invitation>(), Arg.Any<CancellationToken>());
        await _emailSender.Received(1).SendInvitationEmailAsync(
            "invited@example.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InviteUser_WhenEmailIsExistingMember_ReturnsConflict()
    {
        User existingUser = User.Create("Bob", "Jones", Email.Create("invited@example.com").Value, OrgId);
        _userRepo.GetByEmailAsync(Arg.Any<Email>(), OrgId).Returns(existingUser);

        Result result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("already a member");
    }

    [Fact]
    public async Task InviteUser_WhenPendingInvitationExists_ReturnsConflict()
    {
        _userRepo.GetByEmailAsync(Arg.Any<Email>(), OrgId).ReturnsNull();
        Invitation existing = Invitation.Create(
            Email.Create("invited@example.com").Value, OrgId, RoleId, InvitedById);
        _invitationRepo.GetPendingByEmailAsync(Arg.Any<Email>(), OrgId).Returns(existing);

        Result result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("already been sent");
    }

    [Fact]
    public async Task InviteUser_WhenInvitingSelf_ReturnsConflict()
    {
        InviteUserCommand selfCommand = new(OrgId, "invited@example.com", RoleId,
            InvitedById: InvitedById);
        User inviter = User.Create("Alice", "Smith",
            Email.Create("invited@example.com").Value, OrgId);
        // Same user Id
        _userRepo.GetByEmailAsync(Arg.Any<Email>(), OrgId).Returns(inviter);

        // The handler detects self-invite via the existing member check
        Result result = await CreateHandler().Handle(selfCommand, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task InviteUser_WhenRoleNotFound_ReturnsNotFound()
    {
        _userRepo.GetByEmailAsync(Arg.Any<Email>(), OrgId).ReturnsNull();
        _invitationRepo.GetPendingByEmailAsync(Arg.Any<Email>(), OrgId).ReturnsNull();
        _roleRepo.GetByIdAsync(RoleId, OrgId).ReturnsNull();

        Result result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }
}
