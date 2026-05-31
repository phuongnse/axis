using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.Provisioning;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class VerifyEmailHandlerTests
{
    private readonly IEmailVerificationTokenStore _tokenStore = Substitute.For<IEmailVerificationTokenStore>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IOrganizationRepository _organizationRepo = Substitute.For<IOrganizationRepository>();
    private readonly ITenantModuleProvisioningRepository _provisioningRepo =
        Substitute.For<ITenantModuleProvisioningRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private VerifyEmailHandler CreateHandler() =>
        new(_tokenStore, _userRepo, _organizationRepo, _provisioningRepo, _roleRepo, _uow);

    private static (User User, Organization Organization) MakeUnverifiedUserWithOrg()
    {
        Email email = Email.Create("alice@acme.com").Value!;
        Organization organization = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("Alice", "Smith", email, organization.Id);
        user.SetPasswordHash("hashed");
        return (user, organization);
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenIsValid_VerifiesEmailAndRaisesDomainEvent()
    {
        (User user, Organization organization) = MakeUnverifiedUserWithOrg();
        Role adminRole = Role.CreateSystem("Admin", organization.Id, ["users:read"]);
        string rawToken = "valid-raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);

        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.Valid, user.Id));
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _organizationRepo.GetByIdAsync(organization.Id).Returns(organization);
        _roleRepo.GetByNameAsync("Admin", organization.Id).Returns(adminRole);

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("alice@acme.com");
        user.IsEmailVerified.Should().BeTrue();
        organization.Status.Should().Be(OrganizationStatus.Provisioning);

        await _provisioningRepo.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<TenantModuleProvisioning>>(rows => rows.Count() == TenantModuleNames.All.Count),
            Arg.Any<CancellationToken>());

        user.DomainEvents.Should().ContainSingle(e => e is OrganizationVerified)
            .Which.Should().BeOfType<OrganizationVerified>()
            .Which.OrganizationId.Should().Be(user.OrganizationId);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _tokenStore.DidNotReceive().InvalidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenIsUnknown_DoesNotSaveOrRaiseEvent()
    {
        string tokenHash = OpaqueTokenGenerator.Hash("unknown-token");
        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.NotFound, null));

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand("unknown-token"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("Invalid verification link");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenExpired_ReturnsExpiredMessage()
    {
        string rawToken = "expired-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.Expired, Guid.NewGuid()));

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("expired");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenAlreadyUsed_ReturnsBusinessRuleFailure()
    {
        (User user, Organization _) = MakeUnverifiedUserWithOrg();
        string rawToken = "used-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.AlreadyUsed, user.Id));

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("already been used");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenUserAlreadyVerified_ReturnsBusinessRuleFailure()
    {
        (User user, Organization _) = MakeUnverifiedUserWithOrg();
        user.VerifyEmail();
        user.ClearDomainEvents();
        string rawToken = "still-valid-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.Valid, user.Id));
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("already been used");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
