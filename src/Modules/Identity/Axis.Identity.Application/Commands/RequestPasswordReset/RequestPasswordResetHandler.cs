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
    IEmailSender emailSender,
    IUnitOfWork uow)
    : ICommandHandler<RequestPasswordResetCommand>
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    public async Task<Result> Handle(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        Result<Email> email = Email.Create(command.Email);
        if (email.IsFailure) return Result.Success(); // no leakage

        User? user = await userRepo.FindByEmailGloballyAsync(email.Value, cancellationToken);
        if (user is null) return Result.Success(); // same message regardless — no enumeration

        // Invalidate any prior tokens before issuing a new one
        await tokenStore.InvalidateAllForUserAsync(user.Id, cancellationToken);

        (string rawToken, string tokenHash) = OpaqueTokenGenerator.Create();

        await tokenStore.CreateAsync(
            user.Id, tokenHash, DateTime.UtcNow.Add(TokenLifetime), cancellationToken);

        await emailSender.SendPasswordResetEmailAsync(
            user.Email.Value, rawToken, cancellationToken);

        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
