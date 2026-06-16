using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.RegisterUser;

public sealed class RegisterUserHandler(
    IUserRepository userRepo,
    IWorkspaceRepository WorkspaceRepo,
    IWorkspaceMembershipRepository membershipRepo,
    IRoleRepository roleRepo,
    IRegistrationIdempotencyRepository idempotencyRepo,
    IEmailVerificationTokenStore verificationTokenStore,
    IWorkspaceRegistrationTokenStore WorkspaceTokenStore,
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

            User user = User.Create(command.FirstName, command.LastName, email.Value);
            user.SetPasswordHash(hasher.Hash(command.Password));
            user.RecordLegalAcceptance(command.AcceptedTermsVersion, command.AcceptedPrivacyVersion);

            string? setupToken = string.IsNullOrWhiteSpace(command.WorkspaceSetupToken)
                ? null
                : command.WorkspaceSetupToken.Trim();
            if (setupToken is not null)
            {
                Result attachResult = await AttachFirstUserToWorkspaceAsync(
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

    private async Task<Result> AttachFirstUserToWorkspaceAsync(
        User user,
        string setupToken,
        CancellationToken cancellationToken)
    {
        string tokenHash = OpaqueTokenGenerator.Hash(setupToken);
        Result<Guid> setupResult =
            await WorkspaceTokenStore.ConsumeFirstUserSetupAsync(
                tokenHash,
                user.Id,
                cancellationToken);

        if (setupResult.IsFailure)
            return Result.Failure(
                setupResult.ErrorCode ?? ErrorCodes.BusinessRule,
                setupResult.Error);

        Guid workspaceId = setupResult.Value;
        Workspace? Workspace = await WorkspaceRepo.GetByIdAsync(workspaceId, cancellationToken);
        if (Workspace is null || !Workspace.AllowsSignIn())
            return Result.Failure(ErrorCodes.BusinessRule, "Workspace is not ready for user setup.");

        Role? adminRole = await roleRepo.GetByNameAsync("Admin", Workspace.Id, cancellationToken);
        if (adminRole is null)
            return Result.Failure(ErrorCodes.BusinessRule, "Workspace is missing the Admin role.");

        WorkspaceMembership membership = WorkspaceMembership.Create(user.Id, Workspace.Id);
        membership.AssignRole(adminRole.Id);
        await membershipRepo.AddAsync(membership, cancellationToken);

        return Result.Success();
    }

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
            user.Id,
            WellKnownSubscriptionPlans.FreeId);
        personalWorkspace.RecordLegalAcceptance(
            command.AcceptedTermsVersion,
            command.AcceptedPrivacyVersion);

        await WorkspaceRepo.AddAsync(personalWorkspace, cancellationToken);

        IReadOnlyList<Role> defaultRoles = WorkspaceRoleCatalog.CreateDefaultRoles(personalWorkspace.Id);
        foreach (Role role in defaultRoles)
        {
            await roleRepo.AddAsync(role, cancellationToken);
        }

        Role adminRole = defaultRoles.Single(role => role.Name == "Admin");
        WorkspaceMembership membership = WorkspaceMembership.Create(user.Id, personalWorkspace.Id);
        membership.AssignRole(adminRole.Id);
        await membershipRepo.AddAsync(membership, cancellationToken);
    }
}
