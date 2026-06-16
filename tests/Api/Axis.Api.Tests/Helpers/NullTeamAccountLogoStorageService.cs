using Axis.Identity.Application.Services;

namespace Axis.Api.Tests.Helpers;

internal sealed class NullTeamAccountLogoStorageService : ITeamAccountLogoStorageService
{
    public Task<string> UploadLogoAsync(Guid teamAccountId, byte[] data, string contentType, CancellationToken ct = default) =>
        Task.FromResult($"https://test.example.com/team-account-logos/{teamAccountId}");

    public Task DeleteLogoAsync(string logoUrl, CancellationToken ct = default) =>
        Task.CompletedTask;
}
