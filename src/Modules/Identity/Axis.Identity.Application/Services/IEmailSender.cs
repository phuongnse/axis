namespace Axis.Identity.Application.Services;

public interface IEmailSender
{
    Task SendVerificationEmailAsync(
        string toEmail,
        string verificationToken,
        string language,
        CancellationToken ct = default);
}
