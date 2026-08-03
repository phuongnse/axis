using System.ComponentModel;
using Axis.Mcp.Api;
using Axis.Mcp.Configuration;
using ModelContextProtocol.Server;

namespace Axis.Mcp.Tools;

[McpServerToolType]
public sealed class AxisMcpRuleLifecycleTools(
    AxisApiClient api,
    AxisMcpMutationGuard mutationGuard)
{
    [McpServerTool(Name = "axis_create_rule_definition")]
    [Description("[WRITE] Create an unpublished workspace rule draft. The authenticated workspace is derived from OAuth claims.")]
    public Task<string> CreateRuleDefinitionAsync(
        CreateRuleDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("CreateRuleDefinition");
        return api.PostJsonAsync("api/rules", input, cancellationToken);
    }

    [McpServerTool(Name = "axis_save_rule_definition_draft")]
    [Description("[WRITE] Save a workspace rule draft with the caller's expected revision. A stale revision is rejected by Axis.")]
    public Task<string> SaveRuleDefinitionDraftAsync(
        [Description("Stable Axis definition key.")] string definitionKey,
        SaveRuleDefinitionDraftInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateDefinitionKey(definitionKey);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Inputs);
        ArgumentNullException.ThrowIfNull(input.Condition);
        mutationGuard.EnsureEnabled("SaveRuleDefinitionDraft");
        return api.PutJsonAsync(
            $"api/rules/{Uri.EscapeDataString(definitionKey)}/draft",
            input,
            cancellationToken);
    }

    private static void ValidateDefinitionKey(string definitionKey) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionKey);
}

[McpServerToolType]
internal sealed class AxisMcpDeferredRuleLifecycleTools(
    AxisApiClient api,
    AxisMcpMutationGuard mutationGuard)
{
    [McpServerTool(Name = "axis_start_rule_definition_draft")]
    [Description("[WRITE] Start the next workspace rule draft from the current published definition using optimistic concurrency.")]
    public Task<string> StartRuleDefinitionDraftAsync(
        [Description("Stable Axis definition key.")] string definitionKey,
        RuleRevisionInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateDefinitionKey(definitionKey);
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("StartRuleDefinitionDraft");
        return api.PostJsonAsync(
            $"api/rules/{Uri.EscapeDataString(definitionKey)}/draft",
            input,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_publish_rule_definition")]
    [Description("[WRITE] Publish the valid workspace rule draft with the caller's expected revision. Axis owns versioning and lifecycle validation.")]
    public Task<string> PublishRuleDefinitionAsync(
        [Description("Stable Axis definition key.")] string definitionKey,
        RuleRevisionInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateDefinitionKey(definitionKey);
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("PublishRuleDefinition");
        return api.PostJsonAsync(
            $"api/rules/{Uri.EscapeDataString(definitionKey)}/publish",
            input,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_archive_rule_definition")]
    [Description("[WRITE] Archive a workspace rule definition with the caller's expected revision. This does not delete historical versions.")]
    public Task<string> ArchiveRuleDefinitionAsync(
        [Description("Stable Axis definition key.")] string definitionKey,
        RuleRevisionInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateDefinitionKey(definitionKey);
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("ArchiveRuleDefinition");
        return api.PostJsonAsync(
            $"api/rules/{Uri.EscapeDataString(definitionKey)}/archive",
            input,
            cancellationToken);
    }

    private static void ValidateDefinitionKey(string definitionKey) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionKey);
}
