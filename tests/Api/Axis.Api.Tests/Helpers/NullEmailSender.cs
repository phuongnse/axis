using Axis.Identity.Application.Services;

namespace Axis.Api.Tests.Helpers;

internal sealed class NullEmailSender : IEmailSender
{
    public Task SendVerificationEmailAsync(string toEmail, string verificationToken, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendInvitationEmailAsync(string toEmail, string teamAccountName, string invitationToken, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendPasswordChangedNotificationAsync(string toEmail, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendTeamAccountDeletionScheduledEmailAsync(
        string toEmail,
        string teamAccountName,
        CancellationToken ct = default) =>
        Task.CompletedTask;
}
