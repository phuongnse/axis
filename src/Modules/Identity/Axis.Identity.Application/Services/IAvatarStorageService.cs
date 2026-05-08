namespace Axis.Identity.Application.Services;

public interface IAvatarStorageService
{
    Task<string> UploadAvatarAsync(Guid userId, byte[] data, string contentType, CancellationToken ct = default);
    Task DeleteAvatarAsync(string avatarUrl, CancellationToken ct = default);
}
