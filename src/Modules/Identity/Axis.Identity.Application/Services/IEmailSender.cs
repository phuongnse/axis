namespace Axis.Identity.Application.Services;

public interface IEmailSender
{
    Task SendVerificationEmailAsync(string toEmail, string verificationToken, CancellationToken ct = default);
}
