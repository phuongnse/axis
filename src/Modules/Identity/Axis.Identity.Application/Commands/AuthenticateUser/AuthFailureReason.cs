namespace Axis.Identity.Application.Commands.AuthenticateUser;

public enum AuthFailureReason
{
    InvalidCredentials,
    AccountLocked,
    AccountDeactivated,
    EmailNotVerified,
    WorkspaceDeleted,
}
