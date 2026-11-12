using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;

namespace Axis.Agent;

/// <summary>
/// The work of the <c>ready</c> state for a request that asked for auto-accept, see <c>docs/reference/authoring-requests.md#accepting</c>. It makes no model call.
/// It asks for the accept of the request's draft with <c>accept_draft</c>, which alone checks the rules of accepting, and polls the accept until it ends. A refusal of
/// the tool leaves the request <c>ready</c> with the outcome <c>refused</c>. A success moves it to <c>accepted</c> with the outcome <c>accepted</c>. A failure with the
/// <see cref="DiagnosticCodes.AcceptConflict"/> diagnostic moves it to <c>working</c> with the outcome <c>conflict</c> and clears its draft, so the working step makes the
/// change again on a new draft of the folder as it is now. Any other failure, a tool error, an accept that has not ended within <see cref="CheckingOptions.MaxPolls"/> polls,
/// a request without a draft and an application the author can no longer list stop the request as <c>failed</c> with the outcome <c>notAccepted</c> and keep its draft,
/// see <c>docs/reference/authoring-requests.md#budgets</c>.
/// </summary>
internal sealed class AcceptingStep(CheckingOptions options, TimeProvider time) : IAuthoringStep
{
    public string State => "ready";

    public string Kind => "accept";

    public async Task<AuthoringStepResult> RunAsync(AuthoringStepContext context, CancellationToken cancellationToken)
    {
        var application = await ModelLoop.FindApplicationNameAsync(context, cancellationToken);
        if (application is null || context.Request.DraftId is not { } draft)
        {
            return NotAccepted();
        }

        var arguments = new JsonObject { ["application"] = application, ["draft"] = draft.ToString("D") };
        var started = await context.Mcp.CallToolAsync("accept_draft", arguments, cancellationToken);
        if (started.IsError)
        {
            return new AuthoringStepResult("ready", "refused");
        }

        if (started.StructuredContent?["request"] is not JsonValue request || !request.TryGetValue<string>(out var requestId))
        {
            return NotAccepted();
        }

        var accept = started.StructuredContent as JsonObject;
        var poll = (JsonObject)arguments.DeepClone();
        poll["request"] = requestId;
        for (var polls = 0; !Ended(accept) && polls < options.MaxPolls; polls++)
        {
            await Task.Delay(options.PollInterval, time, cancellationToken);
            var answer = await context.Mcp.CallToolAsync("accept_draft", (JsonObject)poll.DeepClone(), cancellationToken);
            if (answer.IsError)
            {
                return NotAccepted();
            }

            accept = answer.StructuredContent as JsonObject;
        }

        if (!Ended(accept))
        {
            return NotAccepted();
        }

        if (accept!["status"]?.GetValue<string>() == "succeeded")
        {
            return new AuthoringStepResult("accepted", "accepted");
        }

        return accept["diagnostics"] is JsonArray diagnostics && diagnostics.OfType<JsonObject>().Any(IsConflict)
            ? new AuthoringStepResult("working", "conflict", DropDraft: true)
            : NotAccepted();
    }

    private static bool Ended(JsonObject? accept) => accept?["status"]?.GetValue<string>() is "succeeded" or "failed";

    private static bool IsConflict(JsonObject diagnostic) =>
        diagnostic["code"] is JsonValue code && code.TryGetValue<string>(out var text) && text == DiagnosticCodes.AcceptConflict;

    private static AuthoringStepResult NotAccepted() => new("failed", "notAccepted");
}
