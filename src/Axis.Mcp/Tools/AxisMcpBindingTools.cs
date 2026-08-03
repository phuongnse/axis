using System.ComponentModel;
using System.Globalization;
using Axis.Mcp.Api;
using Axis.Mcp.Configuration;
using ModelContextProtocol.Server;

namespace Axis.Mcp.Tools;

[McpServerToolType]
public sealed class AxisMcpBindingReadTools(AxisApiClient api)
{
    [McpServerTool(Name = "axis_list_rule_binding_usage")]
    [Description("[READ] List bindings that use one exact rule definition version in the authenticated workspace.")]
    public Task<string> ListRuleBindingUsageAsync(
        [Description("Stable Axis definition key.")] string definitionKey,
        [Description("Exact published rule version.")] int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionKey);
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), "version must be greater than zero.");

        string path = $"api/rules/{Uri.EscapeDataString(definitionKey)}/bindings"
            + AxisApiQuery.Build(("version", version.ToString(CultureInfo.InvariantCulture)));
        return api.GetJsonAsync(path, cancellationToken);
    }
}

[McpServerToolType]
public sealed class AxisMcpBindingWriteTools(
    AxisApiClient api,
    AxisMcpMutationGuard mutationGuard)
{
    [McpServerTool(Name = "axis_create_rule_binding")]
    [Description("[WRITE] Bind one exact published rule version to a consumer target. The authenticated workspace is derived from OAuth claims.")]
    public Task<string> CreateRuleBindingAsync(
        CreateRuleBindingInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("CreateRuleBinding");
        return api.PostJsonAsync("api/rule-bindings", input, cancellationToken);
    }

    [McpServerTool(Name = "axis_update_rule_binding")]
    [Description("[WRITE] Update one binding using the caller's expected revision. The referenced rule version remains exact and immutable.")]
    public Task<string> UpdateRuleBindingAsync(
        Guid bindingId,
        UpdateRuleBindingInput input,
        CancellationToken cancellationToken = default)
    {
        if (bindingId == Guid.Empty)
            throw new ArgumentException("bindingId must be a non-empty UUID.", nameof(bindingId));
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("UpdateRuleBinding");
        return api.PutJsonAsync(
            $"api/rule-bindings/{bindingId:D}",
            input,
            cancellationToken);
    }
}

// Kept out of the registration list until the binding delete contract gains
// an expected-revision check for unattended mutation safety.
[McpServerToolType]
internal sealed class AxisMcpDeferredBindingTools(
    AxisApiClient api,
    AxisMcpMutationGuard mutationGuard)
{
    [McpServerTool(Name = "axis_delete_rule_binding")]
    [Description("[WRITE/DESTRUCTIVE] Delete one rule binding by UUID. This removes only the connection, not the rule definition or version.")]
    public Task<string> DeleteRuleBindingAsync(
        Guid bindingId,
        CancellationToken cancellationToken = default)
    {
        if (bindingId == Guid.Empty)
            throw new ArgumentException("bindingId must be a non-empty UUID.", nameof(bindingId));
        mutationGuard.EnsureEnabled("DeleteRuleBinding");
        return api.DeleteJsonAsync(
            $"api/rule-bindings/{bindingId:D}",
            cancellationToken);
    }
}
