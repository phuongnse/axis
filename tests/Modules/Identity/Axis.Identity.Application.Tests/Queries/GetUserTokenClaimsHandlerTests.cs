using Axis.Identity.Application.Queries.GetUserTokenClaims;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class GetUserTokenClaimsHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly ITenantMembershipRepository _membershipRepo = Substitute.For<ITenantMembershipRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();

    private static readonly Guid TenantId = Guid.NewGuid();

    private GetUserTokenClaimsHandler CreateHandler() => new(_userRepo, _membershipRepo, _roleRepo);

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFound()
    {
        Guid userId = Guid.NewGuid();
        _userRepo.GetByIdPlatformWideAsync(userId, Arg.Any<CancellationToken>()).ReturnsNull();

        Result<UserTokenClaimsDto> result = await CreateHandler().Handle(
            new GetUserTokenClaimsQuery(userId, TenantId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenUserInactive_ReturnsNotFound()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value);
        user.Deactivate();
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        Result<UserTokenClaimsDto> result = await CreateHandler().Handle(
            new GetUserTokenClaimsQuery(user.Id, TenantId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhentenantIdOmitted_UsesUserTenant()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value);
        Role role = Role.CreateSystem("Editor", TenantId, ["workflow:definition:read"]);
        TenantMembership membership = TenantMembership.Create(user.Id, TenantId);
        membership.AssignRole(role.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _roleRepo.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), TenantId, Arg.Any<CancellationToken>())
            .Returns(new List<Role> { role });

        Result<UserTokenClaimsDto> result = await CreateHandler().Handle(
            new GetUserTokenClaimsQuery(user.Id, tenantId: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.tenantId.Should().Be(TenantId);
        result.Value.Permissions.Should().Contain("workflow:definition:read");
    }

    [Fact]
    public async Task Handle_WhentenantIdMismatchesUser_ReturnsBusinessRuleFailure()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        Guid otherTenantId = Guid.NewGuid();
        Result<UserTokenClaimsDto> result = await CreateHandler().Handle(
            new GetUserTokenClaimsQuery(user.Id, otherTenantId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        await _roleRepo.DidNotReceive().GetByIdsAsync(
            Arg.Any<IEnumerable<Guid>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUserActive_ReturnsTokenClaims()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value);
        Role editor = Role.CreateSystem("Editor", TenantId, ["workflow:definition:read", "workflow:definition:write"]);
        TenantMembership membership = TenantMembership.Create(user.Id, TenantId);
        membership.AssignRole(editor.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _membershipRepo.GetByUserAndTenantAsync(user.Id, TenantId, Arg.Any<CancellationToken>()).Returns(membership);
        _roleRepo.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), TenantId, Arg.Any<CancellationToken>())
            .Returns(new List<Role> { editor });

        Result<UserTokenClaimsDto> result = await CreateHandler().Handle(
            new GetUserTokenClaimsQuery(user.Id, TenantId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(user.Id);
        result.Value.Email.Should().Be("ada@example.com");
        result.Value.FullName.Should().Be("Ada Lovelace");
        result.Value.Permissions.Should().BeEquivalentTo(
            ["workflow:definition:read", "workflow:definition:write"],
            opts => opts.WithoutStrictOrdering());
    }
}
