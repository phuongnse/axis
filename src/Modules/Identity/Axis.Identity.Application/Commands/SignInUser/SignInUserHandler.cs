using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.SignInUser;

public sealed class SignInUserHandler(
    IUserRepository userRepo,
    IWorkspaceRepository workspaceRepo,
    IPasswordHasher hasher)
    : ICommandHandler<SignInUserCommand, SignInSuccessDto>
{
    public const string GenericCredentialError = "Email or password is incorrect.";
    public const string VerificationRequiredError = "Email verification is required before sign-in.";
    public const string AccountUnavailableError = "Account is not available for sign-in.";

    public async Task<Result<SignInSuccessDto>> Handle(
        SignInUserCommand command,
        CancellationToken cancellationToken)
    {
        Result<Email> email = Email.Create(command.Email);
        if (email.IsFailure)
        {
            return Result.Failure<SignInSuccessDto>(
                ErrorCodes.InvalidInput,
                "Email must be a valid email address.");
        }

        User? user = await userRepo.FindByEmailGloballyAsync(email.Value, cancellationToken);
        if (user is null
            || user.Status != UserStatus.Active
            || string.IsNullOrWhiteSpace(user.PasswordHash)
            || !hasher.Verify(command.Password, user.PasswordHash))
        {
            return Result.Failure<SignInSuccessDto>(
                ErrorCodes.BusinessRule,
                GenericCredentialError);
        }

        if (!user.IsEmailVerified)
        {
            return Result.Failure<SignInSuccessDto>(
                ErrorCodes.BusinessRule,
                VerificationRequiredError);
        }

        Workspace? personalWorkspace = await workspaceRepo.GetPersonalByOwnerUserIdAsync(
            user.Id,
            cancellationToken);
        if (personalWorkspace is null || !personalWorkspace.AllowsSignIn())
        {
            return Result.Failure<SignInSuccessDto>(
                ErrorCodes.BusinessRule,
                AccountUnavailableError);
        }

        return Result.Success(new SignInSuccessDto(
            user.Id,
            personalWorkspace.Id,
            user.Email.Value,
            user.FullName,
            SignInNextStep.Dashboard));
    }
}
