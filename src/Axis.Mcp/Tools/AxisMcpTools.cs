using System.ComponentModel;
using System.Globalization;
using Axis.Mcp.Api;
using ModelContextProtocol.Server;

namespace Axis.Mcp.Tools;

[McpServerToolType]
public sealed class AxisMcpTools(AxisApiClient api)
{
    private static readonly string[] RuleOrigins = ["System", "Workspace"];
    private static readonly string[] RuleStatuses = ["Draft", "Published", "Archived"];

    [McpServerTool(Name = "axis_get_current_user")]
    [Description("Get the authenticated Axis user and current workspace context from the server.")]
    public Task<string> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        api.GetJsonAsync("api/users/me", cancellationToken);

    [McpServerTool(Name = "axis_list_rules")]
    [Description("List system and workspace rule definitions visible to the authenticated Axis workspace.")]
    public Task<string> ListRulesAsync(
        [Description("One-based result page.")] int page = 1,
        [Description("Number of rules to return, from 1 to 100.")] int pageSize = 20,
        [Description("Optional rule origin: System or Workspace.")] string? origin = null,
        [Description("Optional lifecycle status: Draft, Published, or Archived.")] string? status = null,
        [Description("Optional name or description search text.")] string? query = null,
        [Description("Optional response language, such as en.")] string? language = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(page, pageSize);
        origin = NormalizeEnumFilter(origin, RuleOrigins, nameof(origin));
        status = NormalizeEnumFilter(status, RuleStatuses, nameof(status));

        string path = "api/rules" + AxisApiQuery.Build(
            ("page", page.ToString(CultureInfo.InvariantCulture)),
            ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
            ("origin", origin),
            ("status", status),
            ("query", query),
            ("language", language));
        return api.GetJsonAsync(path, cancellationToken);
    }

    [McpServerTool(Name = "axis_get_rule")]
    [Description("Get one system or workspace rule definition, including its condition, inputs, output contract, and versions.")]
    public Task<string> GetRuleAsync(
        [Description("Stable Axis definition key.")] string definitionKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionKey);
        return api.GetJsonAsync($"api/rules/{Uri.EscapeDataString(definitionKey)}", cancellationToken);
    }

    [McpServerTool(Name = "axis_project_rule_condition")]
    [Description("Validate a typed rule condition and return the server-owned visual and textual projection. This does not persist anything.")]
    public Task<string> ProjectRuleConditionAsync(
        [Description("Version of the typed expression language used by the condition.")] int expressionLanguageVersion,
        [Description("Declared rule inputs used by condition references.")] IReadOnlyList<RuleDraftInput> inputs,
        [Description("Canonical rule condition AST using the existing Axis rule contract.")] RuleConditionNodeInput condition,
        [Description("Response language, such as en.")] string language = "en",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(condition);

        ProjectRuleConditionRequest request = new(
            expressionLanguageVersion,
            inputs,
            condition,
            language);
        return api.PostJsonAsync("api/rules/condition/project", request, cancellationToken);
    }

    [McpServerTool(Name = "axis_simulate_rule")]
    [Description("Simulate a rule draft or exact published version with typed inputs. This is deterministic and side-effect-free.")]
    public Task<string> SimulateRuleAsync(
        [Description("Stable Axis definition key.")] string definitionKey,
        [Description("Typed rule input values keyed by the rule input key.")] IReadOnlyDictionary<string, RuleInputValue> inputs,
        [Description("Optional exact published version; omit to simulate the current draft or default version.")] int? definitionVersion = null,
        [Description("Optional correlation id for the simulation response.")] string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionKey);
        ArgumentNullException.ThrowIfNull(inputs);

        SimulateRuleRequest request = new(
            definitionVersion,
            inputs,
            correlationId ?? Guid.NewGuid().ToString("N"));
        return api.PostJsonAsync(
            $"api/rules/{Uri.EscapeDataString(definitionKey)}/simulate",
            request,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_list_business_object_definitions")]
    [Description("List business-object definitions visible to the authenticated Axis workspace.")]
    public Task<string> ListBusinessObjectDefinitionsAsync(
        [Description("One-based result page.")] int page = 1,
        [Description("Number of definitions to return, from 1 to 100.")] int pageSize = 20,
        [Description("Optional object name or key search text.")] string? query = null,
        [Description("Optional response language, such as en.")] string? language = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(page, pageSize);

        string path = "api/business-object-definitions" + AxisApiQuery.Build(
            ("page", page.ToString(CultureInfo.InvariantCulture)),
            ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
            ("query", query),
            ("language", language));
        return api.GetJsonAsync(path, cancellationToken);
    }

    [McpServerTool(Name = "axis_get_business_object_definition")]
    [Description("Get one business-object definition, including fields, rules, and its latest published version.")]
    public Task<string> GetBusinessObjectDefinitionAsync(
        [Description("Business-object definition UUID.")] string id,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(id, out Guid definitionId))
            throw new ArgumentException("id must be a valid business-object definition UUID.", nameof(id));

        return api.GetJsonAsync(
            $"api/business-object-definitions/{definitionId:D}",
            cancellationToken);
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page), "page must be greater than zero.");
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "pageSize must be between 1 and 100.");
    }

    private static string? NormalizeEnumFilter(
        string? value,
        IReadOnlyList<string> allowedValues,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string? normalized = allowedValues.FirstOrDefault(
            allowed => string.Equals(allowed, value, StringComparison.OrdinalIgnoreCase));
        return normalized ?? throw new ArgumentException(
            $"{parameterName} must be one of: {string.Join(", ", allowedValues)}.",
            parameterName);
    }

    private sealed record ProjectRuleConditionRequest(
        int ExpressionLanguageVersion,
        IReadOnlyList<RuleDraftInput> Inputs,
        RuleConditionNodeInput Condition,
        string Language);

    private sealed record SimulateRuleRequest(
        int? DefinitionVersion,
        IReadOnlyDictionary<string, RuleInputValue> Inputs,
        string CorrelationId);
}
