using System.ComponentModel;
using Axis.Mcp.Api;
using ModelContextProtocol.Server;

namespace Axis.Mcp.Tools;

[McpServerToolType]
public sealed class AxisMcpReferenceTools(AxisApiClient api)
{
    [McpServerTool(Name = "axis_get_legal_versions")]
    [Description("[READ] Get the current Terms of Service and Privacy Policy versions exposed by Axis.")]
    public Task<string> GetLegalVersionsAsync(CancellationToken cancellationToken = default) =>
        api.GetJsonAsync("api/legal/versions", cancellationToken);

    [McpServerTool(Name = "axis_get_rule_expression_language")]
    [Description("[READ] Get the server-owned rule expression language, operators, functions, limits, and documentation metadata.")]
    public Task<string> GetRuleExpressionLanguageAsync(CancellationToken cancellationToken = default) =>
        api.GetJsonAsync("api/rules/expression-language", cancellationToken);

    [McpServerTool(Name = "axis_search_rule_expression_guide")]
    [Description("[READ] Search the server-owned rule expression guide for the current authoring context.")]
    public Task<string> SearchRuleExpressionGuideAsync(
        [Description("Expression language version used by the authoring context.")] int expressionLanguageVersion,
        [Description("Optional rule definition key used to resolve the current input context.")] string? definitionKey,
        [Description("Declared rule inputs available to the guide context.")] IReadOnlyList<RuleInputDefinitionInput> inputs,
        [Description("Optional search text.")] string? query,
        [Description("Response language, such as en.")] string language = "en",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        SearchRuleExpressionGuideInput request = new(
            expressionLanguageVersion,
            definitionKey,
            inputs,
            query,
            language);
        return api.PostJsonAsync("api/rules/expression-language/guide", request, cancellationToken);
    }
}
