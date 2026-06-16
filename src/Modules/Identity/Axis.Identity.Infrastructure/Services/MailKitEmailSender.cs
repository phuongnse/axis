using Axis.Identity.Application.Services;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class MailKitEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendVerificationEmailAsync(string toEmail, string verificationToken, CancellationToken ct = default)
    {
        string subject = "Verify your email address";
        string body = $"Click the link to verify your email: {GetBaseUrl()}/auth/verify?token={verificationToken}";
        await SendAsync(toEmail, subject, body, ct);
    }

    public async Task SendInvitationEmailAsync(string toEmail, string tenantName, string invitationToken, CancellationToken ct = default)
    {
        string subject = $"You've been invited to join {tenantName} on Axis";
        string body = $"Click the link to accept your invitation: {GetBaseUrl()}/invitations/accept?token={invitationToken}";
        await SendAsync(toEmail, subject, body, ct);
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        string subject = "Reset your Axis password";
        string body = $"Click the link to reset your password (valid for 1 hour): {GetBaseUrl()}/auth/reset-password?token={resetToken}";
        await SendAsync(toEmail, subject, body, ct);
    }

    public async Task SendPasswordChangedNotificationAsync(string toEmail, CancellationToken ct = default)
    {
        string subject = "Your Axis password was changed";
        string body = "Your password was recently changed. If this wasn't you, contact support immediately.";
        await SendAsync(toEmail, subject, body, ct);
    }

    public async Task SendTenantDeletionScheduledEmailAsync(
        string toEmail,
        string TenantName,
        CancellationToken ct = default)
    {
        string subject = $"Deletion scheduled for {TenantName}";
        string body =
            "Your Tenant has been scheduled for deletion. All data will be permanently removed in 30 days.";
        await SendAsync(toEmail, subject, body, ct);
    }

    private async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct)
    {
        IConfigurationSection smtp = configuration.GetSection("Email:Smtp");
        MimeMessage message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtp["From"] ?? "noreply@axis.app"));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using SmtpClient client = new SmtpClient();
        await client.ConnectAsync(smtp["Host"] ?? "localhost", int.Parse(smtp["Port"] ?? "587"), cancellationToken: ct);

        if (!string.IsNullOrEmpty(smtp["Username"]))
            await client.AuthenticateAsync(smtp["Username"]!, smtp["Password"] ?? string.Empty, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }

    private string GetBaseUrl() =>
        configuration["App:BaseUrl"] ?? "https://app.axis.io";
}
