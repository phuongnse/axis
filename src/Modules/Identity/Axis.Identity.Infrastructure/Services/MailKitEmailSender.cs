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

    public async Task SendInvitationEmailAsync(string toEmail, string teamAccountName, string invitationToken, CancellationToken ct = default)
    {
        string subject = $"You've been invited to join {teamAccountName} on Axis";
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

    public async Task SendTeamAccountDeletionScheduledEmailAsync(
        string toEmail,
        string teamAccountName,
        CancellationToken ct = default)
    {
        string subject = $"Deletion scheduled for {teamAccountName}";
        string body =
            "Your team account has been scheduled for deletion. All data will be permanently removed in 30 days.";
        await SendAsync(toEmail, subject, body, ct);
    }

    private async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct)
    {
        IConfigurationSection email = configuration.GetSection("Email");
        IConfigurationSection smtp = configuration.GetSection("Email:Smtp");
        string fromAddress = email["FromAddress"] ?? smtp["FromAddress"] ?? smtp["From"] ?? "noreply@axis.app";
        string? fromName = email["FromName"] ?? smtp["FromName"];
        string host = email["Host"] ?? smtp["Host"] ?? "localhost";
        string portValue = email["Port"] ?? smtp["Port"] ?? "587";
        string? username = email["Username"] ?? smtp["Username"];
        string password = email["Password"] ?? smtp["Password"] ?? string.Empty;

        if (!int.TryParse(portValue, out int port))
        {
            throw new InvalidOperationException("Email SMTP port must be an integer.");
        }

        MimeMessage message = new MimeMessage();
        message.From.Add(string.IsNullOrWhiteSpace(fromName)
            ? MailboxAddress.Parse(fromAddress)
            : new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using SmtpClient client = new SmtpClient();
        await client.ConnectAsync(host, port, cancellationToken: ct);

        if (!string.IsNullOrEmpty(username))
        {
            await client.AuthenticateAsync(username, password, ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }

    private string GetBaseUrl() =>
        configuration["App:BaseUrl"] ?? "https://app.axis.io";
}
