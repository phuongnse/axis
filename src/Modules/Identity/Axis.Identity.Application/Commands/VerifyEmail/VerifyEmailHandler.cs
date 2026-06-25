using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed class VerifyEmailHandler(
    IEmailVerificationTokenStore tokenStore,
    IUserRepository userRepo,
    IWorkspaceRepository WorkspaceRepo,
    IUnitOfWork uow)
    : ICommandHandler<VerifyEmailCommand, VerifyEmailSuccessDto>
{
    public async Task<Result<VerifyEmailSuccessDto>> Handle(
        VerifyEmailCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link.");

        string tokenHash = OpaqueTokenGenerator.Hash(command.Token.Trim());
        EmailVerificationTokenResolveResult resolved =
            await tokenStore.ResolveForVerificationAsync(tokenHash, cancellationToken);

        return resolved.State switch
        {
            EmailVerificationTokenState.NotFound =>
                Result.Failure<VerifyEmailSuccessDto>(
                    ErrorCodes.BusinessRule,
                    "Invalid verification link."),
            EmailVerificationTokenState.Expired =>
                Result.Failure<VerifyEmailSuccessDto>(
                    ErrorCodes.BusinessRule,
                    "This verification link has expired. Please request a new verification email."),
            EmailVerificationTokenState.AlreadyUsed =>
                Result.Failure<VerifyEmailSuccessDto>(
                    ErrorCodes.BusinessRule,
                    "This link has already been used. Please sign in."),
            EmailVerificationTokenState.Valid => await VerifyUserAsync(
                resolved.UserId!.Value,
                cancellationToken),
            _ => Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link."),
        };
    }

    private async Task<Result<VerifyEmailSuccessDto>> VerifyUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        User? user = await userRepo.GetByIdPlatformWideAsync(userId, cancellationToken);
        if (user is null)
            return Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link.");

        if (user.IsEmailVerified)
        {
            return Result.Failure<VerifyEmailSuccessDto>(
                ErrorCodes.BusinessRule,
                "This link has already been used. Please sign in.");
        }

        Workspace? personalWorkspace = await WorkspaceRepo.GetPersonalByOwnerUserIdAsync(user.Id, cancellationToken);
        if (personalWorkspace is null)
        {
            return Result.Failure<VerifyEmailSuccessDto>(
                ErrorCodes.BusinessRule,
                "The account does not have a workspace.");
        }

        if (personalWorkspace.Status == WorkspaceStatus.PendingVerification)
        {
            personalWorkspace.ActivateAfterOwnerVerification();
        }
        else if (!personalWorkspace.AllowsSignIn())
        {
            return Result.Failure<VerifyEmailSuccessDto>(
                ErrorCodes.BusinessRule,
                "Workspace is not ready for sign-in.");
        }

        user.VerifyEmail();

        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success(new VerifyEmailSuccessDto(
            user.Id,
            personalWorkspace.Id,
            user.Email.Value,
            user.FullName,
            VerifyEmailNextStep.Dashboard));
    }
}
