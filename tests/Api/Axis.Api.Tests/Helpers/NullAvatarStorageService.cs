using Axis.Identity.Application.Services;

namespace Axis.Api.Tests.Helpers;

internal sealed class NullAvatarStorageService : IAvatarStorageService
{
    public Task<string> UploadAvatarAsync(Guid userId, byte[] data, string contentType, CancellationToken ct = default) =>
        Task.FromResult($"https://test.example.com/avatars/{userId}");

    public Task DeleteAvatarAsync(string avatarUrl, CancellationToken ct = default) =>
        Task.CompletedTask;
}
