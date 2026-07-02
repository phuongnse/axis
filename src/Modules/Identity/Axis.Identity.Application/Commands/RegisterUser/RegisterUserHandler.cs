using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.RegisterUser;

public sealed class RegisterUserHandler(
    IUserRepository userRepo,
    IWorkspaceRepository workspaceRepo,
    IRegistrationIdempotencyRepository idempotencyRepo,
    IEmailVerificationTokenStore verificationTokenStore,
    IWorkspaceSlugGenerator slugGenerator,
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

            User user = User.Create(command.FullName.Trim(), email.Value);
            user.SetPasswordHash(hasher.Hash(command.Password));
            user.RecordLegalAcceptance(command.AcceptedTermsVersion, command.AcceptedPrivacyVersion);

            await CreatePersonalWorkspaceAsync(user, command, cancellationToken);

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

    private async Task CreatePersonalWorkspaceAsync(
        User user,
        RegisterUserCommand command,
        CancellationToken cancellationToken)
    {
        WorkspaceSlug slug = await slugGenerator.GenerateUniqueSlugAsync(
            user.FullName,
            cancellationToken);
        Workspace personalWorkspace = Workspace.CreatePersonal(
            user.FullName,
            slug,
            user.Email,
            user.Id);
        personalWorkspace.RecordLegalAcceptance(
            command.AcceptedTermsVersion,
            command.AcceptedPrivacyVersion);

        await workspaceRepo.AddAsync(personalWorkspace, cancellationToken);
    }
}
