namespace Axis.Identity.Application.Services;

public sealed record ServiceAssertionAuthenticationRequest(string ClientId, string Assertion, string TokenEndpointAudience);
public sealed record ServiceAssertionAuthenticationResult(Guid ServiceIdentityId, Guid WorkspaceId, Guid KeyId, DateTime AccessTokenExpiresAt);
public interface IServiceClientAssertionAuthentication
{
    Task<ServiceAssertionAuthenticationResult?> AuthenticateAsync(ServiceAssertionAuthenticationRequest request, CancellationToken ct = default);
    Task<bool> HasActiveAuthorityAsync(Guid serviceIdentityId, Guid keyId, CancellationToken ct = default);
}
