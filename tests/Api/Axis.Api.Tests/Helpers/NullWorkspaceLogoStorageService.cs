using Axis.Identity.Application.Services;

namespace Axis.Api.Tests.Helpers;

internal sealed class NullWorkspaceLogoStorageService : IWorkspaceLogoStorageService
{
    public Task<string> UploadLogoAsync(Guid workspaceId, byte[] data, string contentType, CancellationToken ct = default) =>
        Task.FromResult($"https://test.example.com/Workspace-logos/{workspaceId}");

    public Task DeleteLogoAsync(string logoUrl, CancellationToken ct = default) =>
        Task.CompletedTask;
}
