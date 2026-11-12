using System.Text.Json.Nodes;
using Axis.Agent.Models;

namespace Axis.Agent;

/// <summary>
/// The work of the <c>working</c> state, see <c>docs/reference/authoring-requests.md#states</c>. It makes the request's draft with <c>create_draft</c> and
/// stores its id in <c>draft_id</c> before the first model call, and a step that runs again uses the stored draft. It then lets the model make the approved
/// change in that draft through the read tools, the write tools and the layer tool of the MCP server and nothing else, and moves the request to
/// <c>checking</c> when the model ends its turn. A step whose model has not ended its turn within <see cref="DraftWork.MaxModelCalls"/> model calls, whose answer
/// stops at the output limit, whose application the author can no longer list or whose draft cannot be made stops the request as <c>failed</c> with the
/// outcome <c>unfinished</c>, see <c>docs/reference/authoring-requests.md#budgets</c>.
/// </summary>
internal sealed class WorkingStep(TenantModelAdapters adapters, AiSettings settings) : IAuthoringStep
{
    public string State => "working";

    public string Kind => "work";

    public async Task<AuthoringStepResult> RunAsync(AuthoringStepContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var application = await ModelLoop.FindApplicationNameAsync(context, cancellationToken);
        if (application is null)
        {
            return new AuthoringStepResult("failed", "unfinished");
        }

        var draft = await FindDraftAsync(context, application, cancellationToken);
        if (draft is null)
        {
            return new AuthoringStepResult("failed", "unfinished");
        }

        return await DraftWork.RunAsync(adapters, settings, context, State, application, draft.Value, FirstMessage(request), cancellationToken);
    }

    // The request's draft is made once. The id is stored before the model's first call, so a step that runs again after an interruption reuses it.
    private static async Task<Guid?> FindDraftAsync(AuthoringStepContext context, string application, CancellationToken cancellationToken)
    {
        if (context.Request.DraftId is { } stored)
        {
            return stored;
        }

        var created = await context.Mcp.CallToolAsync("create_draft", new JsonObject { ["application"] = application }, cancellationToken);
        if (created.IsError
            || created.StructuredContent?["id"] is not JsonValue id || !id.TryGetValue<string>(out var text) || !Guid.TryParse(text, out var draft))
        {
            return null;
        }

        await context.SetDraftAsync(draft, cancellationToken);
        return draft;
    }

    private static string FirstMessage(AuthoringRequestToWork request) =>
        "The request:\n" + request.Text + "\n\nThe approved plan:\n" + (request.Plan ?? "");
}
