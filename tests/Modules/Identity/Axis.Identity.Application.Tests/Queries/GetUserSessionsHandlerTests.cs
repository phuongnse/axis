using Axis.Identity.Application.Commands.RevokeSession;
using Axis.Identity.Application.Queries.GetUserSessions;
using Axis.Identity.Application.Services;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetUserSessionsHandlerTests
{
    private readonly ISessionStore _sessionStore = Substitute.For<ISessionStore>();

    private static readonly Guid UserId = Guid.NewGuid();

    private GetUserSessionsHandler CreateHandler() => new(_sessionStore);

    [Fact]
    public async Task Returns_sessions_from_store()
    {
        var sessions = new List<UserSession>
        {
            new("session-1", UserId, "Chrome / Windows", DateTime.UtcNow, DateTime.UtcNow.AddDays(7), true),
            new("session-2", UserId, "Safari / iOS", DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddDays(6), false),
        };
        _sessionStore.GetByUserAsync(UserId, "current-token", Arg.Any<CancellationToken>())
            .Returns(sessions);

        var result = await CreateHandler().Handle(
            new GetUserSessionsQuery(UserId, "current-token"),
            CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].IsCurrentSession.Should().BeTrue();
    }
}

public class RevokeSessionHandlerTests
{
    private readonly ISessionStore _sessionStore = Substitute.For<ISessionStore>();

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    private RevokeSessionHandler CreateHandler() => new(_sessionStore);

    [Fact]
    public async Task Revokes_session_via_store()
    {
        await CreateHandler().Handle(
            new RevokeSessionCommand("session-1", UserId),
            CancellationToken.None);

        await _sessionStore.Received(1).RevokeAsync("session-1", UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revoke_all_delegates_to_store()
    {
        await CreateHandler().Handle(
            new RevokeSessionCommand(null, UserId),
            CancellationToken.None);

        await _sessionStore.Received(1).RevokeAllAsync(UserId, Arg.Any<CancellationToken>());
    }
}
