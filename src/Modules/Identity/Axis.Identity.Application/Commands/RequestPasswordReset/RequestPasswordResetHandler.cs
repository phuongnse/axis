using System.Security.Cryptography;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.RequestPasswordReset;

public sealed class RequestPasswordResetHandler(
    IUserRepository userRepo,
    IPasswordResetTokenStore tokenStore,
    IEmailSender emailSender)
    : ICommandHandler<RequestPasswordResetCommand>
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    public async Task Handle(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        var email = Email.Create(command.Email);
        if (email.IsFailure) return; // no leakage per US-027

        var user = await userRepo.FindByEmailGloballyAsync(email.Value, cancellationToken);
        if (user is null) return; // same message regardless — no enumeration

        // Invalidate any prior tokens before issuing a new one (per US-027 edge case)
        await tokenStore.InvalidateAllForUserAsync(user.Id, cancellationToken);

        // Generate a cryptographically random opaque token
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rawToken)));

        await tokenStore.CreateAsync(
            user.Id, tokenHash, DateTime.UtcNow.Add(TokenLifetime), cancellationToken);

        await emailSender.SendPasswordResetEmailAsync(
            user.Email.Value, rawToken, cancellationToken);
    }
}
