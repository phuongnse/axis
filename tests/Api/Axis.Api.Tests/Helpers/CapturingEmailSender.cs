using System.Collections.Concurrent;
using Axis.Identity.Application.Services;

namespace Axis.Api.Tests.Helpers;

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

    public Task SendInvitationEmailAsync(string toEmail, string tenantName, string invitationToken, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendPasswordChangedNotificationAsync(string toEmail, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendTenantDeletionScheduledEmailAsync(
        string toEmail,
        string TenantName,
        CancellationToken ct = default) =>
        Task.CompletedTask;
}
