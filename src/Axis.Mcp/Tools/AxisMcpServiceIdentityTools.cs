using System.ComponentModel;
using Axis.Mcp.Api;
using Axis.Mcp.Configuration;
using ModelContextProtocol.Server;

namespace Axis.Mcp.Tools;

[McpServerToolType]
public sealed class AxisMcpServiceIdentityReadTools(AxisApiClient api)
{
    [McpServerTool(Name = "axis_list_service_identities")]
    [Description("[READ] List non-secret service-identity lifecycle projections in the authenticated current Workspace.")]
    public Task<string> ListServiceIdentitiesAsync(CancellationToken cancellationToken = default) =>
        api.GetJsonAsync("api/service-identities", cancellationToken);

    [McpServerTool(Name = "axis_get_service_identity")]
    [Description("[READ] Get one non-secret service-identity lifecycle projection in the authenticated current Workspace.")]
    public Task<string> GetServiceIdentityAsync(
        [Description("Service identity UUID returned by Axis.")] Guid serviceIdentityId,
        CancellationToken cancellationToken = default) =>
        api.GetJsonAsync(
            $"api/service-identities/{RequireId(serviceIdentityId, nameof(serviceIdentityId)):D}",
            cancellationToken);

    private static Guid RequireId(Guid value, string parameterName) =>
        value != Guid.Empty
            ? value
            : throw new ArgumentException("A non-empty UUID is required.", parameterName);
}

[McpServerToolType]
public sealed class AxisMcpServiceIdentityWriteTools(
    AxisApiClient api,
    AxisMcpMutationGuard mutationGuard)
{
    [McpServerTool(Name = "axis_create_service_identity")]
    [Description("[WRITE] Create one service identity in the authenticated current Workspace. This accepts no private key, credential, token, user ID, or Workspace ID.")]
    public Task<string> CreateServiceIdentityAsync(
        CreateServiceIdentityInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ClientId);
        mutationGuard.EnsureEnabled("CreateServiceIdentity");
        return api.PostJsonAsync(
            "api/service-identities",
            new CreateServiceIdentityRequest(input.ClientId),
            cancellationToken);
    }

    [McpServerTool(Name = "axis_add_service_identity_key")]
    [Description("[WRITE] Add one public ES256 JWK to an active service identity. Private key material and service credentials are never accepted.")]
    public Task<string> AddServiceIdentityKeyAsync(
        Guid serviceIdentityId,
        AddServiceIdentityKeyInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.PublicJwk);
        mutationGuard.EnsureEnabled("AddServiceIdentityKey");
        return api.PostJsonAsync(
            $"api/service-identities/{RequireId(serviceIdentityId, nameof(serviceIdentityId)):D}/keys",
            new AddServiceIdentityKeyRequest(input.ExpectedRevision, input.PublicJwk),
            cancellationToken);
    }

    [McpServerTool(Name = "axis_revoke_service_identity_key")]
    [Description("[WRITE/DESTRUCTIVE] Irrevocably revoke one public signing key using the current service-identity revision.")]
    public Task<string> RevokeServiceIdentityKeyAsync(
        Guid serviceIdentityId,
        Guid keyId,
        ExpectedRevisionInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("RevokeServiceIdentityKey");
        return api.PostJsonAsync(
            $"api/service-identities/{RequireId(serviceIdentityId, nameof(serviceIdentityId)):D}/keys/{RequireId(keyId, nameof(keyId)):D}/revoke",
            input,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_revoke_service_identity")]
    [Description("[WRITE/DESTRUCTIVE] Irrevocably revoke one service identity and its intrinsic current-Workspace grant using the current revision.")]
    public Task<string> RevokeServiceIdentityAsync(
        Guid serviceIdentityId,
        ExpectedRevisionInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("RevokeServiceIdentity");
        return api.PostJsonAsync(
            $"api/service-identities/{RequireId(serviceIdentityId, nameof(serviceIdentityId)):D}/revoke",
            input,
            cancellationToken);
    }

    private static Guid RequireId(Guid value, string parameterName) =>
        value != Guid.Empty
            ? value
            : throw new ArgumentException("A non-empty UUID is required.", parameterName);

    private sealed record CreateServiceIdentityRequest(string ClientId);
    private sealed record AddServiceIdentityKeyRequest(int ExpectedRevision, string PublicJwk);
}
