using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.Tenancy;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class VerifyEmailHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ITenantSchemaProvisioner _tenantProvisioner = Substitute.For<ITenantSchemaProvisioner>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private VerifyEmailHandler CreateHandler() => new(_userRepo, _uow, _tenantProvisioner);

    private static User MakeUnverifiedUser()
    {
        User user = User.Create("Alice", "Smith", Email.Create("alice@acme.com").Value, OrgId);
        user.SetPasswordHash("hashed");
        return user;
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenIsValid_VerifiesEmail()
    {
        User user = MakeUnverifiedUser();
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);

        Result result = await CreateHandler().Handle(
            new VerifyEmailCommand(user.Id.ToString()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.IsEmailVerified.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenFormatIsInvalid_ReturnsBusinessRuleFailure()
    {
        Result result = await CreateHandler().Handle(
            new VerifyEmailCommand("not-a-guid"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("Invalid verification link");
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenNotFound_ReturnsBusinessRuleFailure()
    {
        _userRepo.GetByIdPlatformWideAsync(Arg.Any<Guid>()).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new VerifyEmailCommand(Guid.NewGuid().ToString()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("Invalid verification link");
    }

    [Fact]
    public async Task VerifyEmail_WhenAlreadyVerified_ReturnsBusinessRuleFailure()
    {
        User user = MakeUnverifiedUser();
        user.VerifyEmail();
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);

        Result result = await CreateHandler().Handle(
            new VerifyEmailCommand(user.Id.ToString()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("already been used");
    }
}
