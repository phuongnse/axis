using Axis.Identity.Application.Services;

namespace Axis.Api.Tests.Helpers;

internal sealed class NullTenantLogoStorageService : ITenantLogoStorageService
{
    public Task<string> UploadLogoAsync(Guid tenantId, byte[] data, string contentType, CancellationToken ct = default) =>
        Task.FromResult($"https://test.example.com/Tenant-logos/{tenantId}");

    public Task DeleteLogoAsync(string logoUrl, CancellationToken ct = default) =>
        Task.CompletedTask;
}
