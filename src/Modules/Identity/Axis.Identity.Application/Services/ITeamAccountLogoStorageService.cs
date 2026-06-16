namespace Axis.Identity.Application.Services;

public interface ITeamAccountLogoStorageService
{
    Task<string> UploadLogoAsync(Guid teamAccountId, byte[] data, string contentType, CancellationToken ct = default);
    Task DeleteLogoAsync(string logoUrl, CancellationToken ct = default);
}
