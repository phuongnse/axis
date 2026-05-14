using Axis.Identity.Application.Commands.RequestPasswordReset;
using Axis.Identity.Application.Commands.ResetPassword;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class RequestPasswordResetHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IPasswordResetTokenStore _tokenStore = Substitute.For<IPasswordResetTokenStore>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private RequestPasswordResetHandler CreateHandler() =>
        new(_userRepo, _tokenStore, _emailSender);

    private static User MakeUser()
    {
        User user = User.Create("Alice", "Smith", Email.Create("alice@acme.com").Value, OrgId);
        user.SetPasswordHash("hashed");
        user.VerifyEmail();
        return user;
    }

    [Fact]
    public async Task Happy_path_creates_token_and_sends_email()
    {
        User user = MakeUser();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        await CreateHandler().Handle(
            new RequestPasswordResetCommand("alice@acme.com"),
            CancellationToken.None);

        await _tokenStore.Received(1).InvalidateAllForUserAsync(user.Id, Arg.Any<CancellationToken>());
        await _tokenStore.Received(1).CreateAsync(
            user.Id, Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _emailSender.Received(1).SendPasswordResetEmailAsync(
            "alice@acme.com", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_email_does_nothing_no_error_leakage()
    {
        // Per US-027: same message regardless of whether email exists
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).ReturnsNull();

        Func<Task> act = async () => await CreateHandler().Handle(
            new RequestPasswordResetCommand("unknown@acme.com"),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _emailSender.DidNotReceive().SendPasswordResetEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task New_request_invalidates_previous_token()
    {
        User user = MakeUser();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        await CreateHandler().Handle(
            new RequestPasswordResetCommand("alice@acme.com"),
            CancellationToken.None);

        // Prior tokens must be invalidated before creating the new one
        await _tokenStore.Received(1).InvalidateAllForUserAsync(user.Id, Arg.Any<CancellationToken>());
    }
}
