using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.AuthenticateUser;

public sealed class AuthenticateUserHandler(
    IUserRepository userRepo,
    IOrganizationRepository organizationRepo,
    IRoleRepository roleRepo,
    IPasswordHasher hasher,
    IUnitOfWork uow)
    : ICommandHandler<AuthenticateUserCommand, AuthenticationResult>
{
    public async Task<Result<AuthenticationResult>> Handle(
        AuthenticateUserCommand command, CancellationToken cancellationToken)
    {
        Result<Email> emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
            return AuthenticationResult.Fail(AuthFailureReason.InvalidCredentials);

        User? user = await userRepo.FindByEmailGloballyAsync(emailResult.Value, cancellationToken);
        if (user is null)
            return AuthenticationResult.Fail(AuthFailureReason.InvalidCredentials);

        if (user.Status == UserStatus.Inactive)
            return AuthenticationResult.Fail(AuthFailureReason.AccountDeactivated);

        if (!user.IsEmailVerified)
            return AuthenticationResult.Fail(AuthFailureReason.EmailNotVerified);

        Organization? organization = await organizationRepo.GetByIdAsync(user.OrganizationId, cancellationToken);
        if (organization is null || !organization.AllowsSignIn())
            return AuthenticationResult.Fail(AuthFailureReason.OrganizationDeleted);

        if (user.IsLockedOut)
            return AuthenticationResult.Fail(AuthFailureReason.AccountLocked, user.LockedUntil);

        if (!hasher.Verify(command.Password, user.PasswordHash ?? string.Empty))
        {
            user.RecordFailedLogin();
            await uow.SaveChangesAsync(cancellationToken);
            return AuthenticationResult.Fail(AuthFailureReason.InvalidCredentials);
        }

        user.ResetFailedLogins();
        await uow.SaveChangesAsync(cancellationToken);

        IReadOnlyList<Role> roles = await roleRepo.GetByIdsAsync(user.RoleIds, user.OrganizationId, cancellationToken);
        List<string> permissions = roles
            .SelectMany(r => r.Permissions)
            .Distinct()
            .ToList();

        return AuthenticationResult.Ok(
            user.Id, user.OrganizationId, user.Email.Value, user.FullName, permissions);
    }
}
