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

    [McpServerTool(Name = "axis_create_rule_definition_version")]
    [Description("[WRITE] Create an immutable version from the current rule draft using the caller's expected revision. Creation does not activate the version.")]
    public Task<string> CreateRuleDefinitionVersionAsync(
        [Description("Stable Axis definition key.")] string definitionKey,
        RuleRevisionInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateDefinitionKey(definitionKey);
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("CreateRuleDefinitionVersion");
        return api.PostJsonAsync(
            $"api/rules/{Uri.EscapeDataString(definitionKey)}/versions",
            input,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_activate_rule_definition_version")]
    [Description("[WRITE] Activate one exact immutable rule version for discovery and new bindings using the caller's expected revision.")]
    public Task<string> ActivateRuleDefinitionVersionAsync(
        [Description("Stable Axis definition key.")] string definitionKey,
        ActivateRuleDefinitionVersionInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateDefinitionKey(definitionKey);
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("ActivateRuleDefinitionVersion");
        return api.PutJsonAsync(
            $"api/rules/{Uri.EscapeDataString(definitionKey)}/active-version",
            input,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_deactivate_rule_definition")]
    [Description("[WRITE] Deactivate a rule definition for new bindings using the caller's expected revision. Existing exact-version bindings are unchanged.")]
    public Task<string> DeactivateRuleDefinitionAsync(
        [Description("Stable Axis definition key.")] string definitionKey,
        RuleRevisionInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateDefinitionKey(definitionKey);
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("DeactivateRuleDefinition");
        return api.DeleteJsonAsync(
            $"api/rules/{Uri.EscapeDataString(definitionKey)}/active-version",
            input,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_archive_rule_definition")]
    [Description("[WRITE/DESTRUCTIVE] Archive a workspace rule definition using the caller's expected revision. Archived exact versions remain available only for historical evaluation.")]
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

[McpServerToolType]
public sealed class AxisMcpRuleReadTools(AxisApiClient api)
{
    [McpServerTool(Name = "axis_simulate_rule_definition_draft")]
    [Description("[READ] Simulate the current workspace rule draft with typed inputs. This is deterministic and side-effect-free.")]
    public Task<string> SimulateRuleDefinitionDraftAsync(
        [Description("Stable Axis definition key.")] string definitionKey,
        SimulateRuleDraftInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateDefinitionKey(definitionKey);
        ArgumentNullException.ThrowIfNull(input);
        return api.PostJsonAsync(
            $"api/rules/{Uri.EscapeDataString(definitionKey)}/draft/simulate",
            input,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_simulate_rule_definition_version")]
    [Description("[READ] Simulate one exact immutable rule version with typed inputs. This is deterministic and side-effect-free.")]
    public Task<string> SimulateRuleDefinitionVersionAsync(
        [Description("Stable Axis definition key.")] string definitionKey,
        [Description("Exact immutable rule version.")] int version,
        SimulateRuleVersionInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateDefinitionKey(definitionKey);
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), "version must be greater than zero.");
        ArgumentNullException.ThrowIfNull(input);
        return api.PostJsonAsync(
            $"api/rules/{Uri.EscapeDataString(definitionKey)}/versions/{version}/simulate",
            input,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_project_rule_authoring")]
    [Description("[READ] Project one rule authoring source into canonical rule logic and safe diagnostics without persistence.")]
    public Task<string> ProjectRuleAuthoringAsync(
        ProjectRuleAuthoringInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return api.PostJsonAsync(
            "api/rules/authoring/project",
            input,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_complete_rule_authoring")]
    [Description("[READ] Complete the server-owned rule authoring language at a cursor position without persistence.")]
    public Task<string> CompleteRuleAuthoringAsync(
        CompleteRuleAuthoringInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return api.PostJsonAsync("api/rules/authoring/complete", input, cancellationToken);
    }

    private static void ValidateDefinitionKey(string definitionKey) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionKey);
}
