namespace Axis.Identity.Application.Services;

public interface IEmailSender
{
    Task SendVerificationEmailAsync(string toEmail, string verificationToken, CancellationToken ct = default);
    Task SendInvitationEmailAsync(string toEmail, string orgName, string invitationToken, CancellationToken ct = default);
}
