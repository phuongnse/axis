namespace Axis.Identity.Application.Services;

public interface IWorkspaceLogoStorageService
{
    Task<string> UploadLogoAsync(Guid workspaceId, byte[] data, string contentType, CancellationToken ct = default);
    Task DeleteLogoAsync(string logoUrl, CancellationToken ct = default);
}
