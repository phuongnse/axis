using System.Security.Cryptography;
using System.Text;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.RequestPasswordReset;

public sealed class RequestPasswordResetHandler(
    IUserRepository userRepo,
    IPasswordResetTokenStore tokenStore,
    IEmailSender emailSender)
    : ICommandHandler<RequestPasswordResetCommand>
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    public async Task<Result> Handle(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        Result<Email> email = Email.Create(command.Email);
        if (email.IsFailure) return Result.Success(); // no leakage per US-027

        User? user = await userRepo.FindByEmailGloballyAsync(email.Value, cancellationToken);
        if (user is null) return Result.Success(); // same message regardless — no enumeration

        // Invalidate any prior tokens before issuing a new one (per US-027 edge case)
        await tokenStore.InvalidateAllForUserAsync(user.Id, cancellationToken);

        // Generate a cryptographically random opaque token
        string rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string tokenHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(rawToken)));

        await tokenStore.CreateAsync(
            user.Id, tokenHash, DateTime.UtcNow.Add(TokenLifetime), cancellationToken);

        await emailSender.SendPasswordResetEmailAsync(
            user.Email.Value, rawToken, cancellationToken);

        return Result.Success();
    }
}
