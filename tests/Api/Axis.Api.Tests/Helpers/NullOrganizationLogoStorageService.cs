using Axis.Identity.Application.Services;

namespace Axis.Api.Tests.Helpers;

internal sealed class NullOrganizationLogoStorageService : IOrganizationLogoStorageService
{
    public Task<string> UploadLogoAsync(Guid organizationId, byte[] data, string contentType, CancellationToken ct = default) =>
        Task.FromResult($"https://test.example.com/org-logos/{organizationId}");

    public Task DeleteLogoAsync(string logoUrl, CancellationToken ct = default) =>
        Task.CompletedTask;
}
