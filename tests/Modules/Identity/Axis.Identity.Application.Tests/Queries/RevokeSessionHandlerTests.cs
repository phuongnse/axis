using Axis.Identity.Application.Commands.RevokeSession;
using Axis.Identity.Application.Services;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class RevokeSessionHandlerTests
{
    private readonly ISessionStore _sessionStore = Substitute.For<ISessionStore>();

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TeamAccountId = Guid.NewGuid();

    private RevokeSessionHandler CreateHandler() => new(_sessionStore);

    [Fact]
    public async Task RevokeSession_WhenSessionIdProvided_RevokesSessionViaStore()
    {
        await CreateHandler().Handle(
            new RevokeSessionCommand("session-1", UserId),
            CancellationToken.None);

        await _sessionStore.Received(1).RevokeAsync("session-1", UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeSession_WhenNoSessionIdProvided_RevokesAllSessionsViaStore()
    {
        await CreateHandler().Handle(
            new RevokeSessionCommand(null, UserId),
            CancellationToken.None);

        await _sessionStore.Received(1).RevokeAllAsync(UserId, Arg.Any<CancellationToken>());
    }
}
