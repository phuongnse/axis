using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.RegisterUser;

public sealed class RegisterUserHandler(
    IUserRepository userRepo,
    ITeamAccountRepository teamAccountRepo,
    ITeamAccountMembershipRepository membershipRepo,
    IRoleRepository roleRepo,
    IRegistrationIdempotencyRepository idempotencyRepo,
    IEmailVerificationTokenStore verificationTokenStore,
    ITeamAccountRegistrationTokenStore teamAccountTokenStore,
    IPasswordHasher hasher,
    IEmailSender emailSender,
    IUnitOfWork uow)
    : ICommandHandler<RegisterUserCommand>
{
    private static readonly TimeSpan VerificationTokenLifetime = TimeSpan.FromHours(24);

    public async Task<Result> Handle(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        string? idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? null
            : command.IdempotencyKey.Trim();

        RegistrationIdempotencyAcquireResult acquireResult = RegistrationIdempotencyAcquireResult.Acquired;
        if (idempotencyKey is not null)
        {
            acquireResult = await idempotencyRepo.AcquireAsync(idempotencyKey, cancellationToken);
            if (acquireResult is RegistrationIdempotencyAcquireResult.AlreadyCompleted
                or RegistrationIdempotencyAcquireResult.InProgress)
            {
                return Result.Success();
            }
        }

        try
        {
            Result<Email> email = Email.Create(command.Email);
            if (email.IsFailure)
            {
                return Result.Failure(ErrorCodes.InvalidInput, "Email must be a valid email address.");
            }

            if (await userRepo.EmailExistsPlatformWideAsync(email.Value, cancellationToken))
            {
                return Result.Failure(
                    ErrorCodes.Conflict,
                    "An account with this email already exists. Sign in instead.");
            }

            User user = User.Create(command.FirstName, command.LastName, email.Value);
            user.SetPasswordHash(hasher.Hash(command.Password));
            user.RecordLegalAcceptance(command.AcceptedTermsVersion, command.AcceptedPrivacyVersion);

            string? setupToken = string.IsNullOrWhiteSpace(command.TeamAccountSetupToken)
                ? null
                : command.TeamAccountSetupToken.Trim();
            if (setupToken is not null)
            {
                Result attachResult = await AttachFirstUserToTeamAccountAsync(
                    user,
                    setupToken,
                    cancellationToken);
                if (attachResult.IsFailure)
                {
                    await MarkIdempotencyFailedIfNeededAsync(
                        idempotencyKey,
                        acquireResult,
                        cancellationToken);
                    return attachResult;
                }
            }

            await userRepo.AddAsync(user, cancellationToken);
            await uow.SaveChangesAsync(cancellationToken);

            (string rawToken, string tokenHash) = OpaqueTokenGenerator.Create();
            await verificationTokenStore.CreateAsync(
                user.Id,
                tokenHash,
                DateTime.UtcNow.Add(VerificationTokenLifetime),
                cancellationToken);

            await emailSender.SendVerificationEmailAsync(
                email.Value.Value,
                rawToken,
                cancellationToken);

            await MarkIdempotencyCompletedIfNeededAsync(idempotencyKey, cancellationToken);
            return Result.Success();
        }
        catch
        {
            await MarkIdempotencyFailedIfNeededAsync(
                idempotencyKey,
                acquireResult,
                cancellationToken);

            throw;
        }
    }

    private Task MarkIdempotencyCompletedIfNeededAsync(
        string? idempotencyKey,
        CancellationToken cancellationToken) =>
        idempotencyKey is null
            ? Task.CompletedTask
            : idempotencyRepo.MarkCompletedAsync(idempotencyKey, cancellationToken);

    private Task MarkIdempotencyFailedIfNeededAsync(
        string? idempotencyKey,
        RegistrationIdempotencyAcquireResult acquireResult,
        CancellationToken cancellationToken) =>
        idempotencyKey is not null && acquireResult == RegistrationIdempotencyAcquireResult.Acquired
            ? idempotencyRepo.MarkFailedAsync(idempotencyKey, cancellationToken)
            : Task.CompletedTask;

    private async Task<Result> AttachFirstUserToTeamAccountAsync(
        User user,
        string setupToken,
        CancellationToken cancellationToken)
    {
        string tokenHash = OpaqueTokenGenerator.Hash(setupToken);
        Result<Guid> setupResult =
            await teamAccountTokenStore.ConsumeFirstUserSetupAsync(
                tokenHash,
                user.Id,
                cancellationToken);

        if (setupResult.IsFailure)
            return Result.Failure(
                setupResult.ErrorCode ?? ErrorCodes.BusinessRule,
                setupResult.Error);

        Guid teamAccountId = setupResult.Value;
        TeamAccount? teamAccount = await teamAccountRepo.GetByIdAsync(teamAccountId, cancellationToken);
        if (teamAccount is null || !teamAccount.AllowsSignIn())
            return Result.Failure(ErrorCodes.BusinessRule, "Team account is not ready for user setup.");

        Role? adminRole = await roleRepo.GetByNameAsync("Admin", teamAccount.Id, cancellationToken);
        if (adminRole is null)
            return Result.Failure(ErrorCodes.BusinessRule, "Team account is missing the Admin role.");

        TeamAccountMembership membership = TeamAccountMembership.Create(user.Id, teamAccount.Id);
        membership.AssignRole(adminRole.Id);
        await membershipRepo.AddAsync(membership, cancellationToken);

        return Result.Success();
    }
}
