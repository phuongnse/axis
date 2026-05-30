using Axis.Identity.Application.Commands.CreateExternalRegistrationSession;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public class CreateExternalRegistrationSessionHandlerTests
{
    private readonly IExternalRegistrationSessionRepository _sessionRepo =
        Substitute.For<IExternalRegistrationSessionRepository>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CreateExternalRegistrationSessionHandler CreateHandler() =>
        new(_sessionRepo, _userRepo, _uow);

    [Fact]
    public async Task CreateExternalRegistrationSession_WhenEmailIsAvailable_CreatesSession()
    {
        _userRepo.EmailExistsPlatformWideAsync(Arg.Any<Email>()).Returns(false);

        Shared.Domain.Primitives.Result<Guid> result = await CreateHandler().Handle(
            new CreateExternalRegistrationSessionCommand(
                ExternalIdentityProvider.Google,
                "google-subject-1",
                "new-user@example.com",
                "New User"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        await _sessionRepo.Received(1).AddAsync(
            Arg.Is<ExternalRegistrationSession>(s =>
                s.Email.Value == "new-user@example.com"
                && s.Provider == ExternalIdentityProvider.Google),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateExternalRegistrationSession_WhenEmailAlreadyExists_ReturnsConflict()
    {
        _userRepo.EmailExistsPlatformWideAsync(Arg.Any<Email>()).Returns(true);

        Shared.Domain.Primitives.Result<Guid> result = await CreateHandler().Handle(
            new CreateExternalRegistrationSessionCommand(
                ExternalIdentityProvider.Google,
                "google-subject-1",
                "existing@example.com",
                "Existing User"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already exists");
        await _sessionRepo.DidNotReceive().AddAsync(
            Arg.Any<ExternalRegistrationSession>(),
            Arg.Any<CancellationToken>());
    }
}
