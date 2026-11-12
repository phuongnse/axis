using System.Text.Json.Nodes;
using Axis.Agent.Models;

namespace Axis.Agent;

/// <summary>
/// The one policy that the working and the review step share for a tool call of the model, see <c>docs/reference/authoring-requests.md#states</c>.
/// </summary>
internal static class DraftToolPolicy
{
    // The tools whose input has a draft. The others have none, and the server refuses an input with a field it does not know.
    private static readonly HashSet<string> _draftTools = new(StringComparer.Ordinal)
    {
        "list_resources",
        "read_resource",
        "read_names",
        "write_resource",
        "delete_resource",
        "write_layer",
        "check_expression",
        "validate_draft",
        "diff_draft",
    };

    // A tool that is not offered, a call that names another application or another draft never reaches the MCP server. A call without a draft gets the request's.
    internal static async Task<ModelToolResult> AnswerAsync(
        AuthoringStepContext context,
        string state,
        IReadOnlySet<string> offered,
        string application,
        Guid draft,
        ModelToolCall call,
        CancellationToken cancellationToken)
    {
        if (!offered.Contains(call.Name))
        {
            return new ModelToolResult(call.Id, $"The tool is not offered in {state}.", true);
        }

        var (arguments, error) = ModelLoop.CheckInput(call, application);
        if (error is not null)
        {
            return new ModelToolResult(call.Id, error, true);
        }

        if (_draftTools.Contains(call.Name))
        {
            arguments ??= [];
            if (!arguments.TryGetPropertyValue("draft", out var named))
            {
                arguments["draft"] = draft.ToString("D");
            }
            else if (!(named is JsonValue value && value.TryGetValue<string>(out var text) && Guid.TryParse(text, out var parsed) && parsed == draft))
            {
                return new ModelToolResult(call.Id, $"The tool may name only the draft '{draft:D}'.", true);
            }
        }

        var result = await context.Mcp.CallToolAsync(call.Name, arguments, cancellationToken);
        return new ModelToolResult(call.Id, result.StructuredContent?.ToJsonString() ?? result.Text, result.IsError);
    }
}
