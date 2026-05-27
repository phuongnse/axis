using System.Collections.Concurrent;
using Axis.Identity.Application.Services;

namespace Axis.Testing.TestDoubles;

/// <summary>Records outbound email tokens for integration test assertions.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<string, string> _verificationTokensByEmail = new(StringComparer.OrdinalIgnoreCase);

    public string? GetVerificationToken(string email) =>
        _verificationTokensByEmail.TryGetValue(email, out string? token) ? token : null;

    public Task SendVerificationEmailAsync(string toEmail, string verificationToken, CancellationToken ct = default)
    {
        _verificationTokensByEmail[toEmail] = verificationToken;
        return Task.CompletedTask;
    }

    public Task SendInvitationEmailAsync(string toEmail, string orgName, string invitationToken, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendPasswordChangedNotificationAsync(string toEmail, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendOrganizationDeletionScheduledEmailAsync(
        string toEmail,
        string organizationName,
        CancellationToken ct = default) =>
        Task.CompletedTask;
}
