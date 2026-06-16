using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.RegisterWorkspace;

public sealed class RegisterWorkspaceHandler(
    IWorkspaceRepository workspaceRepo,
    ISubscriptionPlanRepository planRepo,
    IRoleRepository roleRepo,
    IRegistrationIdempotencyRepository idempotencyRepo,
    IWorkspaceRegistrationTokenStore workspaceTokenStore,
    IWorkspaceSlugGenerator slugGenerator,
    IEmailSender emailSender,
    IUnitOfWork uow)
    : ICommandHandler<RegisterWorkspaceCommand>
{
    private static readonly TimeSpan VerificationTokenLifetime = TimeSpan.FromHours(24);

    public async Task<Result> Handle(RegisterWorkspaceCommand command, CancellationToken cancellationToken)
    {
        string? idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? null
            : command.IdempotencyKey.Trim();

        RegistrationIdempotencyAcquireResult acquireResult = RegistrationIdempotencyAcquireResult.Acquired;
        if (idempotencyKey is not null)
        {
            acquireResult = await idempotencyRepo.AcquireAsync(idempotencyKey, cancellationToken);
            if (acquireResult == RegistrationIdempotencyAcquireResult.AlreadyCompleted
                || acquireResult == RegistrationIdempotencyAcquireResult.InProgress)
            {
                return Result.Success();
            }
        }

        try
        {
            Result<Email> email = Email.Create(command.WorkspaceContactEmail);
            if (email.IsFailure)
            {
                return Result.Failure(ErrorCodes.InvalidInput, "Email must be a valid email address.");
            }

            WorkspaceSlug slug =
                await slugGenerator.GenerateUniqueSlugAsync(command.WorkspaceName, cancellationToken);

            Guid planId = await ResolveSubscriptionPlanIdAsync(command.SubscriptionPlanId, cancellationToken);

            Workspace workspace = Workspace.RegisterTeamForContactVerification(
                command.WorkspaceName,
                slug,
                email.Value,
                planId,
                command.AcceptedTermsVersion,
                command.AcceptedPrivacyVersion);
            await workspaceRepo.AddAsync(workspace, cancellationToken);

            foreach (Role role in WorkspaceRoleCatalog.CreateDefaultRoles(workspace.Id))
            {
                await roleRepo.AddAsync(role, cancellationToken);
            }

            (string rawToken, string tokenHash) = OpaqueTokenGenerator.Create();
            await workspaceTokenStore.CreateVerificationAsync(
                workspace.Id,
                tokenHash,
                DateTime.UtcNow.Add(VerificationTokenLifetime),
                cancellationToken);

            await uow.SaveChangesAsync(cancellationToken);

            await emailSender.SendVerificationEmailAsync(
                email.Value.Value,
                rawToken,
                cancellationToken);

            await MarkIdempotencyCompletedIfNeededAsync(idempotencyKey, cancellationToken);
            return Result.Success();
        }
        catch
        {
            if (idempotencyKey is not null && acquireResult == RegistrationIdempotencyAcquireResult.Acquired)
                await idempotencyRepo.MarkFailedAsync(idempotencyKey, cancellationToken);

            throw;
        }
    }

    private Task MarkIdempotencyCompletedIfNeededAsync(
        string? idempotencyKey,
        CancellationToken cancellationToken) =>
        idempotencyKey is null
            ? Task.CompletedTask
            : idempotencyRepo.MarkCompletedAsync(idempotencyKey, cancellationToken);

    private async Task<Guid> ResolveSubscriptionPlanIdAsync(Guid? requestedPlanId, CancellationToken cancellationToken)
    {
        if (requestedPlanId is Guid planId && planId != Guid.Empty)
        {
            SubscriptionPlan? plan = await planRepo.GetByIdAsync(planId, cancellationToken);
            if (plan is not null && plan.IsActive && plan.IsAvailableForNewSignups)
                return plan.Id;
        }

        SubscriptionPlan? freePlan =
            await planRepo.GetByIdAsync(WellKnownSubscriptionPlans.FreeId, cancellationToken);
        if (freePlan is not null)
            return freePlan.Id;

        return WellKnownSubscriptionPlans.FreeId;
    }
}
