using Axis.Identity.Application.Queries.GetUserSessions;
using Axis.Identity.Application.Services;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetUserSessionsHandlerTests
{
    private readonly ISessionStore _sessionStore = Substitute.For<ISessionStore>();

    private static readonly Guid UserId = Guid.NewGuid();

    private GetUserSessionsHandler CreateHandler() => new(_sessionStore);

    [Fact]
    public async Task GetUserSessions_WhenCalled_ReturnsSessionsFromStore()
    {
        List<UserSession> sessions =
        [
            new("session-1", UserId, "Chrome / Windows", DateTime.UtcNow, DateTime.UtcNow.AddDays(7), true),
            new("session-2", UserId, "Safari / iOS", DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddDays(6), false),
        ];
        _sessionStore.GetByUserAsync(UserId, "current-token", Arg.Any<CancellationToken>())
            .Returns(sessions);

        IReadOnlyList<UserSession> result = await CreateHandler().Handle(
            new GetUserSessionsQuery(UserId, "current-token"),
            CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].IsCurrentSession.Should().BeTrue();
    }

    [Fact]
    public async Task GetUserSessions_WhenNoActiveSessions_ReturnsEmptyList()
    {
        _sessionStore.GetByUserAsync(UserId, "current-token", Arg.Any<CancellationToken>())
            .Returns(new List<UserSession>());

        IReadOnlyList<UserSession> result = await CreateHandler().Handle(
            new GetUserSessionsQuery(UserId, "current-token"),
            CancellationToken.None);

        result.Should().BeEmpty();
    }
}
