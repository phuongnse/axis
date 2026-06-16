namespace Axis.Identity.Application.Services;

public interface ITenantLogoStorageService
{
    Task<string> UploadLogoAsync(Guid tenantId, byte[] data, string contentType, CancellationToken ct = default);
    Task DeleteLogoAsync(string logoUrl, CancellationToken ct = default);
}
