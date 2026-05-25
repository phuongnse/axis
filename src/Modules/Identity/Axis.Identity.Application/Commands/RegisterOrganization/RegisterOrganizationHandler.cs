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
    IPasswordHasher hasher,
    IEmailSender emailSender,
    IUnitOfWork uow)
    : ICommandHandler<RegisterOrganizationCommand>
{
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
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            bool claimed = await idempotencyRepo.TryClaimAsync(command.IdempotencyKey, cancellationToken);
            if (!claimed)
                return Result.Success();
        }

        // Per US-001: always show the same screen — no leakage if email already exists
        Result<Email> email = Email.Create(command.AdminEmail);
        if (email.IsFailure)
            return Result.Success(); // validation layer handles this before handler is reached

        if (await userRepo.EmailExistsPlatformWideAsync(email.Value, cancellationToken))
            return Result.Success(); // silently succeed — same confirmation screen shown

        // Generate unique slug from org name
        OrganizationSlug slug = await GenerateUniqueSlugAsync(command.OrgName, cancellationToken);

        // Create Organization
        Organization org = Organization.Create(command.OrgName, slug, email.Value);
        await orgRepo.AddAsync(org, cancellationToken);

        // Seed 4 system roles for this org
        Role adminRole = Role.CreateSystem("Admin", org.Id, AdminPermissions);
        Role editorRole = Role.CreateSystem("Editor", org.Id, EditorPermissions);
        Role viewerRole = Role.CreateSystem("Viewer", org.Id, ViewerPermissions);
        Role endUserRole = Role.CreateSystem("End User", org.Id, EndUserPermissions);

        await roleRepo.AddAsync(adminRole, cancellationToken);
        await roleRepo.AddAsync(editorRole, cancellationToken);
        await roleRepo.AddAsync(viewerRole, cancellationToken);
        await roleRepo.AddAsync(endUserRole, cancellationToken);

        // Create admin user
        string passwordHash = hasher.Hash(command.Password);
        User user = User.Create(command.AdminFirstName, command.AdminLastName, email.Value, org.Id);
        user.SetPasswordHash(passwordHash);
        user.AssignRole(adminRole.Id);
        await userRepo.AddAsync(user, cancellationToken);

        await uow.SaveChangesAsync(cancellationToken);

        // Send verification email (fire-and-forget style — failure doesn't roll back registration)
        await emailSender.SendVerificationEmailAsync(
            email.Value.Value,
            verificationToken: user.Id.ToString(), // simplified; real impl uses a dedicated token
            cancellationToken);

        return Result.Success();
    }

    private async Task<OrganizationSlug> GenerateUniqueSlugAsync(string orgName, CancellationToken ct)
    {
        string baseSlug = GenerateSlugFromName(orgName);
        Result<OrganizationSlug> candidate = OrganizationSlug.Create(baseSlug);

        if (candidate.IsSuccess && !await orgRepo.SlugExistsAsync(candidate.Value, ct))
            return candidate.Value;

        // Append random suffix until unique
        for (int i = 0; i < 10; i++)
        {
            string suffix = Random.Shared.Next(1000, 9999).ToString();
            Result<OrganizationSlug> withSuffix = OrganizationSlug.Create($"{baseSlug}-{suffix}");
            if (withSuffix.IsSuccess && !await orgRepo.SlugExistsAsync(withSuffix.Value, ct))
                return withSuffix.Value;
        }

        // Fallback: use a UUID-based slug
        return OrganizationSlug.Create($"org-{Guid.NewGuid():N}"[..20]).Value;
    }

    private static string GenerateSlugFromName(string name) =>
        Regex.Replace(name.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-")
            .Trim('-');
}
