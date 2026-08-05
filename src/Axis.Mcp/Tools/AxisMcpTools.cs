using System.ComponentModel;
using System.Globalization;
using Axis.Mcp.Api;
using ModelContextProtocol.Server;

namespace Axis.Mcp.Tools;

[McpServerToolType]
public sealed class AxisMcpTools(AxisApiClient api)
{
    private static readonly string[] RuleOrigins = ["BuiltIn", "Workspace"];
    private static readonly string[] RuleStatuses = ["Draft", "Inactive", "Active", "Archived"];

    [McpServerTool(Name = "axis_get_current_user")]
    [Description("[READ] Get the authenticated Axis user and current workspace context from the server.")]
    public Task<string> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        api.GetJsonAsync("api/users/me", cancellationToken);

    [McpServerTool(Name = "axis_list_rules")]
    [Description("[READ] List built-in and workspace rule definitions visible to the authenticated Axis workspace.")]
    public Task<string> ListRulesAsync(
        [Description("One-based result page.")] int page = 1,
        [Description("Number of rules to return, from 1 to 100.")] int pageSize = 20,
        [Description("Optional rule origin: BuiltIn or Workspace.")] string? origin = null,
        [Description("Optional lifecycle status: Draft, Inactive, Active, or Archived.")] string? status = null,
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
    [Description("[READ] Get one built-in or workspace rule definition, including its condition, inputs, output contract, and versions.")]
    public Task<string> GetRuleAsync(
        [Description("Stable Axis definition key.")] string definitionKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionKey);
        return api.GetJsonAsync($"api/rules/{Uri.EscapeDataString(definitionKey)}", cancellationToken);
    }

    [McpServerTool(Name = "axis_project_rule_condition")]
    [Description("[READ] Validate a typed rule condition and return the server-owned visual and textual projection. This does not persist anything.")]
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

}
