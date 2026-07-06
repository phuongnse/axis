using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ResendVerificationEmail;

public sealed class ResendVerificationEmailHandler(
    IUserRepository userRepo,
    IEmailVerificationTokenStore tokenStore,
    IEmailSender emailSender,
    IResendVerificationRateLimiter rateLimiter)
    : ICommandHandler<ResendVerificationEmailCommand>
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    public async Task<Result> Handle(ResendVerificationEmailCommand command, CancellationToken cancellationToken)
    {
        Result<Email> email = Email.Create(command.Email);
        // Keep resend responses indistinguishable to avoid account enumeration.
        if (email.IsFailure) return Result.Success();

        Result rateLimit = await rateLimiter.TryRecordResendAsync(email.Value.Value, cancellationToken);
        if (rateLimit.IsFailure)
            return rateLimit;

        User? user = await userRepo.FindByEmailGloballyAsync(email.Value, cancellationToken);
        if (user is null || user.IsEmailVerified) return Result.Success();

        await tokenStore.InvalidateAllForUserAsync(user.Id, cancellationToken);

        (string rawToken, string tokenHash) = OpaqueTokenGenerator.Create();
        await tokenStore.CreateAsync(
            user.Id, tokenHash, DateTime.UtcNow.Add(TokenLifetime), cancellationToken);

        await emailSender.SendVerificationEmailAsync(
            user.Email.Value,
            rawToken,
            user.LanguagePreference?.Value ?? UserLanguage.DefaultValue,
            cancellationToken);

        return Result.Success();
    }
}
