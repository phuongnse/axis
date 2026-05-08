using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;

namespace Axis.Api.Tests.Helpers;

internal sealed class NullUnitOfWork(IdentityDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        context.SaveChangesAsync(ct);
}

internal sealed class NullEmailSender : IEmailSender
{
    public Task SendVerificationEmailAsync(string toEmail, string verificationToken, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendInvitationEmailAsync(string toEmail, string orgName, string invitationToken, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendPasswordChangedNotificationAsync(string toEmail, CancellationToken ct = default) =>
        Task.CompletedTask;
}

internal sealed class NullAvatarStorageService : IAvatarStorageService
{
    public Task<string> UploadAvatarAsync(Guid userId, byte[] data, string contentType, CancellationToken ct = default) =>
        Task.FromResult($"https://test.example.com/avatars/{userId}");

    public Task DeleteAvatarAsync(string avatarUrl, CancellationToken ct = default) =>
        Task.CompletedTask;
}
