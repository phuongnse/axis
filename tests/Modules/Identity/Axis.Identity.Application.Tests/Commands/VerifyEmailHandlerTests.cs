using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class VerifyEmailHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private VerifyEmailHandler CreateHandler() => new(_userRepo, _uow);

    private static User MakeUnverifiedUser()
    {
        User user = User.Create("Alice", "Smith", Email.Create("alice@acme.com").Value, OrgId);
        user.SetPasswordHash("hashed");
        return user;
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenIsValid_VerifiesEmailAndRaisesDomainEvent()
    {
        User user = MakeUnverifiedUser();
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);

        Result result = await CreateHandler().Handle(
            new VerifyEmailCommand(user.Id.ToString()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.IsEmailVerified.Should().BeTrue();

        // Verify the OrganizationVerified domain event was raised. IdentityUnitOfWork
        // maps it to OrganizationVerifiedEvent (Avro) and publishes via Kafka (ADR-019).
        user.DomainEvents.Should().ContainSingle(e => e is OrganizationVerified)
            .Which.Should().BeOfType<OrganizationVerified>()
            .Which.OrganizationId.Should().Be(user.OrganizationId);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenFormatIsInvalid_DoesNotSaveOrRaiseEvent()
    {
        Result result = await CreateHandler().Handle(
            new VerifyEmailCommand("not-a-guid"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("Invalid verification link");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
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
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenAlreadyVerified_ReturnsBusinessRuleFailure()
    {
        User user = MakeUnverifiedUser();
        user.VerifyEmail();
        user.ClearDomainEvents();
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);

        Result result = await CreateHandler().Handle(
            new VerifyEmailCommand(user.Id.ToString()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("already been used");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
