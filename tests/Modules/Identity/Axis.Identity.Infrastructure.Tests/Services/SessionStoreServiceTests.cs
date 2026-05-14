using Axis.Identity.Application.Services;
using FluentAssertions;
using NSubstitute;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Identity.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SessionStoreService"/> — verifies that token manager
/// calls are delegated correctly for session enumeration and revocation.
/// </summary>
public class SessionStoreServiceTests
{
    private readonly IOpenIddictTokenManager _tokenManager = Substitute.For<IOpenIddictTokenManager>();
    private readonly SessionStoreService _sut;

    public SessionStoreServiceTests()
    {
        _sut = new SessionStoreService(_tokenManager);
    }

    [Fact]
    public async Task GetByUserAsync_returns_only_valid_refresh_tokens()
    {
        Guid userId = Guid.NewGuid();
        object validToken = new();
        object revokedToken = new();
        object accessToken = new();

        _tokenManager.FindBySubjectAsync(userId.ToString(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnum(validToken, revokedToken, accessToken));

        // valid refresh token
        _tokenManager.GetTypeAsync(validToken, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>(TokenTypeHints.RefreshToken));
        _tokenManager.GetStatusAsync(validToken, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>(Statuses.Valid));
        _tokenManager.GetIdAsync(validToken, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>("tok-1"));
        _tokenManager.GetExpirationDateAsync(validToken, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<DateTimeOffset?>(DateTimeOffset.UtcNow.AddDays(7)));
        _tokenManager.GetCreationDateAsync(validToken, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<DateTimeOffset?>(DateTimeOffset.UtcNow));

        // revoked refresh token
        _tokenManager.GetTypeAsync(revokedToken, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>(TokenTypeHints.RefreshToken));
        _tokenManager.GetStatusAsync(revokedToken, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>(Statuses.Revoked));

        // access token — should be skipped
        _tokenManager.GetTypeAsync(accessToken, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>(TokenTypeHints.AccessToken));
        _tokenManager.GetStatusAsync(accessToken, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>(Statuses.Valid));

        IReadOnlyList<UserSession> sessions = await _sut.GetByUserAsync(userId, "tok-1");

        sessions.Should().HaveCount(1);
        sessions[0].SessionId.Should().Be("tok-1");
        sessions[0].IsCurrentSession.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAsync_calls_token_manager_revoke()
    {
        Guid userId = Guid.NewGuid();
        object token = new();

        _tokenManager.FindByIdAsync("sess-abc", Arg.Any<CancellationToken>())
            .Returns(token);

        await _sut.RevokeAsync("sess-abc", userId);

        await _tokenManager.Received(1).TryRevokeAsync(token, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_does_nothing_when_session_not_found()
    {
        _tokenManager.FindByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((object?)null);

        await _sut.RevokeAsync("missing-id", Guid.NewGuid());

        await _tokenManager.DidNotReceive().TryRevokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAllAsync_revokes_each_token_for_user()
    {
        Guid userId = Guid.NewGuid();
        object t1 = new();
        object t2 = new();

        _tokenManager.FindBySubjectAsync(userId.ToString(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnum(t1, t2));

        await _sut.RevokeAllAsync(userId);

        await _tokenManager.Received(1).TryRevokeAsync(t1, Arg.Any<CancellationToken>());
        await _tokenManager.Received(1).TryRevokeAsync(t2, Arg.Any<CancellationToken>());
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<object> AsyncEnum(params object[] items)
    {
        foreach (object item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
