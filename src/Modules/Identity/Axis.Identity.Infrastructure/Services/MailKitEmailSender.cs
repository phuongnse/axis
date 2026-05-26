using Axis.Identity.Application.Services;
using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Configuration;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class MailKitEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendVerificationEmailAsync(string toEmail, string verificationToken, CancellationToken ct = default)
    {
        var subject = "Verify your email address";
        var body = $"Click the link to verify your email: {GetBaseUrl()}/auth/verify?token={verificationToken}";
        await SendAsync(toEmail, subject, body, ct);
    }

    public async Task SendInvitationEmailAsync(string toEmail, string orgName, string invitationToken, CancellationToken ct = default)
    {
        var subject = $"You've been invited to join {orgName} on Axis";
        var body = $"Click the link to accept your invitation: {GetBaseUrl()}/invitations/accept?token={invitationToken}";
        await SendAsync(toEmail, subject, body, ct);
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        var subject = "Reset your Axis password";
        var body = $"Click the link to reset your password (valid for 1 hour): {GetBaseUrl()}/auth/reset-password?token={resetToken}";
        await SendAsync(toEmail, subject, body, ct);
    }

    public async Task SendPasswordChangedNotificationAsync(string toEmail, CancellationToken ct = default)
    {
        var subject = "Your Axis password was changed";
        var body = "Your password was recently changed. If this wasn't you, contact support immediately.";
        await SendAsync(toEmail, subject, body, ct);
    }

    public async Task SendOrganizationDeletionScheduledEmailAsync(
        string toEmail,
        string organizationName,
        CancellationToken ct = default)
    {
        string subject = $"Deletion scheduled for {organizationName}";
        string body =
            "Your organization has been scheduled for deletion. All data will be permanently removed in 30 days.";
        await SendAsync(toEmail, subject, body, ct);
    }

    private async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct)
    {
        var smtp = configuration.GetSection("Email:Smtp");

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtp["From"] ?? "noreply@axis.app"));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp["Host"] ?? "localhost", int.Parse(smtp["Port"] ?? "587"), cancellationToken: ct);

        if (!string.IsNullOrEmpty(smtp["Username"]))
            await client.AuthenticateAsync(smtp["Username"]!, smtp["Password"] ?? string.Empty, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }

    private string GetBaseUrl() =>
        configuration["App:BaseUrl"] ?? "https://app.axis.io";
}
