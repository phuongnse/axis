namespace Axis.Identity.Application.Services;

public interface IOrganizationLogoStorageService
{
    Task<string> UploadLogoAsync(Guid organizationId, byte[] data, string contentType, CancellationToken ct = default);
    Task DeleteLogoAsync(string logoUrl, CancellationToken ct = default);
}
