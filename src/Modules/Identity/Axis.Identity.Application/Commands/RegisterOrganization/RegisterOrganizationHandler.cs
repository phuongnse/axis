using System.Text.RegularExpressions;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.RegisterOrganization;

public sealed class RegisterOrganizationHandler(
    IOrganizationRepository orgRepo,
    IUserRepository userRepo,
    IRoleRepository roleRepo,
    IRegistrationIdempotencyRepository idempotencyRepo,
    IEmailVerificationTokenStore verificationTokenStore,
    IPasswordHasher hasher,
    IEmailSender emailSender,
    IUnitOfWork uow)
    : ICommandHandler<RegisterOrganizationCommand>
{
    private static readonly TimeSpan VerificationTokenLifetime = TimeSpan.FromHours(24);
    // Permission catalogue — matches F04 docs
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
        "roles:read", "roles:write"
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

    public async Task<Result> Handle(RegisterOrganizationCommand command, CancellationToken cancellationToken)
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
            Result<Email> email = Email.Create(command.AdminEmail);
            if (email.IsFailure)
            {
                await MarkIdempotencyCompletedIfNeededAsync(idempotencyKey, cancellationToken);
                return Result.Success();
            }

            if (await userRepo.EmailExistsPlatformWideAsync(email.Value, cancellationToken))
            {
                await MarkIdempotencyCompletedIfNeededAsync(idempotencyKey, cancellationToken);
                return Result.Success();
            }

            OrganizationSlug slug = await GenerateUniqueSlugAsync(command.OrgName, cancellationToken);

            Organization org = Organization.Create(command.OrgName, slug, email.Value);
            await orgRepo.AddAsync(org, cancellationToken);

            Role adminRole = Role.CreateSystem("Admin", org.Id, AdminPermissions);
            Role editorRole = Role.CreateSystem("Editor", org.Id, EditorPermissions);
            Role viewerRole = Role.CreateSystem("Viewer", org.Id, ViewerPermissions);
            Role endUserRole = Role.CreateSystem("End User", org.Id, EndUserPermissions);

            await roleRepo.AddAsync(adminRole, cancellationToken);
            await roleRepo.AddAsync(editorRole, cancellationToken);
            await roleRepo.AddAsync(viewerRole, cancellationToken);
            await roleRepo.AddAsync(endUserRole, cancellationToken);

            string passwordHash = hasher.Hash(command.Password);
            User user = User.Create(command.AdminFirstName, command.AdminLastName, email.Value, org.Id);
            user.SetPasswordHash(passwordHash);
            user.AssignRole(adminRole.Id);
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

    private async Task<OrganizationSlug> GenerateUniqueSlugAsync(string orgName, CancellationToken ct)
    {
        string baseSlug = GenerateSlugFromName(orgName);
        Result<OrganizationSlug> candidate = OrganizationSlug.Create(baseSlug);

        if (candidate.IsSuccess && !await orgRepo.SlugExistsAsync(candidate.Value, ct))
            return candidate.Value;

        for (int i = 0; i < 10; i++)
        {
            string suffix = Random.Shared.Next(1000, 9999).ToString();
            Result<OrganizationSlug> withSuffix = OrganizationSlug.Create($"{baseSlug}-{suffix}");
            if (withSuffix.IsSuccess && !await orgRepo.SlugExistsAsync(withSuffix.Value, ct))
                return withSuffix.Value;
        }

        return OrganizationSlug.Create($"org-{Guid.NewGuid():N}"[..20]).Value;
    }

    private static string GenerateSlugFromName(string name) =>
        Regex.Replace(name.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-")
            .Trim('-');
}
