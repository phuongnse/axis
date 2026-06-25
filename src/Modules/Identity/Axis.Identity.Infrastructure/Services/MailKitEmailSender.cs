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

    private async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct)
    {
        IConfigurationSection email = configuration.GetSection("Email");
        IConfigurationSection smtp = email.GetSection("Smtp");

        static string? FirstConfigured(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        static string FirstConfiguredOrDefault(string defaultValue, params string?[] values) =>
            FirstConfigured(values) ?? defaultValue;

        string fromAddress = FirstConfiguredOrDefault(
            "noreply@axis.app",
            email["FromAddress"],
            smtp["FromAddress"],
            smtp["From"],
            email["From"]);
        string? fromName = FirstConfigured(email["FromName"], smtp["FromName"]);
        string host = FirstConfiguredOrDefault("localhost", email["Host"], smtp["Host"]);
        string portValue = FirstConfiguredOrDefault("587", email["Port"], smtp["Port"]);
        string? username = FirstConfigured(email["Username"], smtp["Username"]);
        string password = FirstConfigured(email["Password"], smtp["Password"]) ?? string.Empty;

        MimeMessage message = new MimeMessage();
        message.From.Add(string.IsNullOrWhiteSpace(fromName)
            ? MailboxAddress.Parse(fromAddress)
            : new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using SmtpClient client = new SmtpClient();
        await client.ConnectAsync(host, int.Parse(portValue), cancellationToken: ct);

        if (username is not null)
            await client.AuthenticateAsync(username, password, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }

    private string GetBaseUrl() =>
        configuration["App:BaseUrl"] ?? "https://app.axis.io";
}
