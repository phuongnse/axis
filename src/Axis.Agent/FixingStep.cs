using System.Text;
using System.Text.Json.Nodes;
using Axis.Agent.Models;

namespace Axis.Agent;

/// <summary>
/// The work of the <c>fixing</c> state, see <c>docs/reference/authoring-requests.md#states</c>. It works on the draft that the working step stored, with the
/// tools and the draft rules of working through <see cref="DraftWork"/>, and never makes a draft. The model's first message holds the request, the approved
/// plan, what failed in the latest check and the findings of the review after it. The request moves to <c>checking</c> when the model ends its turn. A step
/// whose model has not ended its turn within <see cref="DraftWork.MaxModelCalls"/> model calls, whose answer stops at the output limit, whose request has no
/// draft or whose application the author can no longer list stops the request as <c>failed</c> with the outcome <c>unfinished</c>, see
/// <c>docs/reference/authoring-requests.md#budgets</c>.
/// </summary>
internal sealed class FixingStep(TenantModelAdapters adapters, AiSettings settings) : IAuthoringStep
{
    public string State => "fixing";

    public string Kind => "fix";

    public async Task<AuthoringStepResult> RunAsync(AuthoringStepContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var application = await ModelLoop.FindApplicationNameAsync(context, cancellationToken);
        if (application is null || request.DraftId is not { } draft)
        {
            return new AuthoringStepResult("failed", "unfinished");
        }

        return await DraftWork.RunAsync(adapters, settings, context, State, application, draft, FirstMessage(request), cancellationToken);
    }

    /// <summary>The model's first message: the request, the plan, the failures of the latest check, and the findings of the review after it.</summary>
    internal static string FirstMessage(AuthoringRequestToWork request)
    {
        var message = new StringBuilder()
            .Append("The request:\n").Append(request.Text)
            .Append("\n\nThe approved plan:\n").Append(request.Plan ?? "")
            .Append("\n\nWhat failed in the last check:\n").Append(Failures(request.LatestChecks));
        if (request.LatestFindings is not null)
        {
            message.Append("\n\nThe review findings:\n").Append(request.LatestFindings);
        }

        return message.Append("\n\nFix every failure and every finding in the draft.").ToString();
    }

    // The compile diagnostics when the compile failed, and the scenario items that did not pass, as the check step stored them.
    private static string Failures(string? checks)
    {
        if (checks is null || JsonNode.Parse(checks) is not JsonObject root)
        {
            return "none";
        }

        var failures = new List<string>();
        if (root["compile"] is JsonObject compile && compile["outcome"]?.GetValue<string>() == "failed" && compile["diagnostics"] is JsonArray diagnostics)
        {
            failures.Add("Compile diagnostics: " + diagnostics.ToJsonString());
        }

        if (root["scenarios"]?["items"] is JsonArray items)
        {
            var failed = items.Where(item => item?["outcome"]?.GetValue<string>() != "passed").ToArray();
            if (failed.Length > 0)
            {
                failures.Add("Scenarios that did not pass: " + new JsonArray(failed.Select(item => item!.DeepClone()).ToArray()).ToJsonString());
            }
        }

        return failures.Count == 0 ? "none" : string.Join("\n", failures);
    }
}
