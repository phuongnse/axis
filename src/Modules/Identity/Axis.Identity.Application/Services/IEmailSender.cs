namespace Axis.Identity.Application.Services;

public interface IEmailSender
{
    Task SendVerificationEmailAsync(string toEmail, string verificationToken, CancellationToken ct = default);
    Task SendInvitationEmailAsync(string toEmail, string teamAccountName, string invitationToken, CancellationToken ct = default);
    Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default);
    Task SendPasswordChangedNotificationAsync(string toEmail, CancellationToken ct = default);
    Task SendTeamAccountDeletionScheduledEmailAsync(string toEmail, string teamAccountName, CancellationToken ct = default);
}
