using System.ComponentModel;
using System.Globalization;
using Axis.Mcp.Api;
using Axis.Mcp.Configuration;
using ModelContextProtocol.Server;

namespace Axis.Mcp.Tools;

[McpServerToolType]
public sealed class AxisMcpBindingReadTools(AxisApiClient api)
{
    [McpServerTool(Name = "axis_get_rule_binding")]
    [Description("[READ] Get one rule binding with its full mappings and revision in the authenticated workspace.")]
    public Task<string> GetRuleBindingAsync(
        [Description("Rule binding UUID.")] Guid bindingId,
        CancellationToken cancellationToken = default)
    {
        if (bindingId == Guid.Empty)
            throw new ArgumentException("bindingId must be a non-empty UUID.", nameof(bindingId));

        return api.GetJsonAsync($"api/rule-bindings/{bindingId:D}", cancellationToken);
    }

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

    [McpServerTool(Name = "axis_delete_rule_binding")]
    [Description("[WRITE/DESTRUCTIVE] Delete one rule binding with the caller's expected revision. This removes only the connection, not the rule definition or version.")]
    public Task<string> DeleteRuleBindingAsync(
        Guid bindingId,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (bindingId == Guid.Empty)
            throw new ArgumentException("bindingId must be a non-empty UUID.", nameof(bindingId));
        if (expectedRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision), "expectedRevision must be greater than zero.");
        mutationGuard.EnsureEnabled("DeleteRuleBinding");
        return api.DeleteJsonAsync(
            $"api/rule-bindings/{bindingId:D}",
            new ExpectedRevisionInput(expectedRevision),
            cancellationToken);
    }
}

[McpServerToolType]
public sealed class AxisMcpBindingEvaluationTools(AxisApiClient api)
{
    [McpServerTool(Name = "axis_evaluate_rule_binding")]
    [Description("[READ] Evaluate one rule binding against a transient typed consumer context. This is deterministic and does not mutate the binding or its rule.")]
    public Task<string> EvaluateRuleBindingAsync(
        Guid bindingId,
        EvaluateRuleBindingInput input,
        CancellationToken cancellationToken = default)
    {
        if (bindingId == Guid.Empty)
            throw new ArgumentException("bindingId must be a non-empty UUID.", nameof(bindingId));
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Context);
        ArgumentNullException.ThrowIfNull(input.Context.Values);
        if (input.BindingRevision is < 1)
            throw new ArgumentOutOfRangeException(nameof(input), "bindingRevision must be greater than zero when supplied.");

        return api.PostJsonAsync(
            $"api/rule-bindings/{bindingId:D}/evaluate",
            input,
            cancellationToken);
    }
}
