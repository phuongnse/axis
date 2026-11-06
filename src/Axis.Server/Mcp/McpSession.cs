using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;

namespace Axis.Server.Mcp;

/// <summary>
/// The MCP session id: the person's subject, the client id (<c>azp</c>) and the protocol version, protected with the
/// platform's data protection keys. The purpose includes the tenant id, so a session id of one tenant never reads on
/// another. The server stores nothing: a later request is checked by reading the id back.
/// </summary>
internal sealed class McpSession(IDataProtectionProvider protection)
{
    private const string Purpose = "Axis.Mcp.Session";

    public string Create(string tenantId, McpSessionData data) =>
        Protector(tenantId).Protect(JsonSerializer.Serialize(data));

    /// <summary>The session in <paramref name="sessionId"/>, or null when it is not a session of this tenant.</summary>
    public McpSessionData? TryRead(string tenantId, string sessionId)
    {
        try
        {
            return JsonSerializer.Deserialize<McpSessionData>(Protector(tenantId).Unprotect(sessionId));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private IDataProtector Protector(string tenantId) => protection.CreateProtector(Purpose, tenantId);
}

/// <summary>What a session id holds: the subject, the client id and the protocol version the session was opened with.</summary>
internal sealed record McpSessionData(
    [property: JsonPropertyName("sub")] string Subject,
    [property: JsonPropertyName("azp")] string? ClientId,
    [property: JsonPropertyName("v")] string Version);
