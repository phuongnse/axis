using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.RegisterTenant;

public sealed class RegisterTenantHandler(
    ITenantRepository tenantRepo,
    ISubscriptionPlanRepository planRepo,
    IRoleRepository roleRepo,
    IRegistrationIdempotencyRepository idempotencyRepo,
    ITenantRegistrationTokenStore tenantTokenStore,
    ITenantSlugGenerator slugGenerator,
    IEmailSender emailSender,
    IUnitOfWork uow)
    : ICommandHandler<RegisterTenantCommand>
{
    private static readonly TimeSpan VerificationTokenLifetime = TimeSpan.FromHours(24);
    // Permission catalogue
    private static readonly string[] AdminPermissions =
    [
        "data_modeling:model:read", "data_modeling:model:write", "data_modeling:model:delete",
        "data_modeling:record:read", "data_modeling:record:write", "data_modeling:record:delete",
        "workflow:definition:read", "workflow:definition:write", "workflow:definition:delete",
        "workflow:trigger:manual",
        "form:definition:read", "form:definition:write", "form:submit",
        "execution:read", "execution:cancel", "execution:retry",
        "page:read", "page:write", "page:publish",
        "users:read", "users:invite", "users:deactivate",
        "roles:read", "roles:write",
        "tenant:settings:read", "tenant:settings:write", "tenant:delete"
    ];

    private static readonly string[] EditorPermissions =
    [
        "data_modeling:model:read", "data_modeling:model:write",
        "data_modeling:record:read", "data_modeling:record:write",
        "workflow:definition:read", "workflow:definition:write", "workflow:trigger:manual",
        "form:definition:read", "form:definition:write",
        "execution:read", "execution:cancel", "execution:retry",
        "page:read", "page:write"
    ];

    private static readonly string[] ViewerPermissions =
    [
        "data_modeling:model:read", "data_modeling:record:read",
        "workflow:definition:read",
        "form:definition:read",
        "execution:read",
        "page:read"
    ];

    private static readonly string[] EndUserPermissions =
    [
        "form:submit"
    ];

    public async Task<Result> Handle(RegisterTenantCommand command, CancellationToken cancellationToken)
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
            Result<Email> email = Email.Create(command.TenantContactEmail);
            if (email.IsFailure)
            {
                return Result.Failure(ErrorCodes.InvalidInput, "Email must be a valid email address.");
            }

            TenantSlug slug =
                await slugGenerator.GenerateUniqueSlugAsync(command.TenantName, cancellationToken);

            Guid planId = await ResolveSubscriptionPlanIdAsync(command.SubscriptionPlanId, cancellationToken);

            Tenant tenant = Tenant.RegisterForContactVerification(
                command.TenantName,
                slug,
                email.Value,
                planId,
                command.AcceptedTermsVersion,
                command.AcceptedPrivacyVersion);
            await tenantRepo.AddAsync(tenant, cancellationToken);

            Role adminRole = Role.CreateSystem("Admin", tenant.Id, AdminPermissions);
            Role editorRole = Role.CreateSystem("Editor", tenant.Id, EditorPermissions);
            Role viewerRole = Role.CreateSystem("Viewer", tenant.Id, ViewerPermissions);
            Role endUserRole = Role.CreateSystem("End User", tenant.Id, EndUserPermissions);

            await roleRepo.AddAsync(adminRole, cancellationToken);
            await roleRepo.AddAsync(editorRole, cancellationToken);
            await roleRepo.AddAsync(viewerRole, cancellationToken);
            await roleRepo.AddAsync(endUserRole, cancellationToken);

            (string rawToken, string tokenHash) = OpaqueTokenGenerator.Create();
            await tenantTokenStore.CreateVerificationAsync(
                tenant.Id,
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
