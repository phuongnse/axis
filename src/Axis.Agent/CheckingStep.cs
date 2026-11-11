using System.Text.Json.Nodes;

namespace Axis.Agent;

/// <summary>
/// The work of the <c>checking</c> state, see <c>docs/reference/authoring-requests.md#states</c>. It makes no model call. It compiles the request's draft
/// with <c>validate_draft</c> and, when no diagnostic is an error, runs every scenario of the application and its modules on it with <c>run_scenarios</c>
/// and polls the run until it ends. It stores <c>{ compile, scenarios }</c> on its step and moves the request to <c>reviewing</c> when both passed, or
/// to <c>fixing</c> when one failed. A run that has not ended within <see cref="CheckingOptions.MaxPolls"/> polls, a tool error, a request without a
/// draft and an application the author can no longer list stop the request as <c>failed</c> with the outcome <c>unfinished</c>, see
/// <c>docs/reference/authoring-requests.md#budgets</c>.
/// </summary>
internal sealed class CheckingStep(CheckingOptions options, TimeProvider time) : IAuthoringStep
{
    public string State => "checking";

    public string Kind => "check";

    public async Task<AuthoringStepResult> RunAsync(AuthoringStepContext context, CancellationToken cancellationToken)
    {
        var application = await ModelLoop.FindApplicationNameAsync(context, cancellationToken);
        if (application is null || context.Request.DraftId is not { } draft)
        {
            return Unfinished();
        }

        var arguments = new JsonObject { ["application"] = application, ["draft"] = draft.ToString("D") };
        var validated = await context.Mcp.CallToolAsync("validate_draft", arguments, cancellationToken);
        if (validated.IsError || validated.StructuredContent?["diagnostics"] is not JsonArray diagnostics)
        {
            return Unfinished();
        }

        // A warning is kept in the checks and does not fail the compile, as in a publish or a test request.
        var compiled = !diagnostics.OfType<JsonObject>().Any(diagnostic => diagnostic["severity"]?.GetValue<string>() == "error");
        var compile = new JsonObject { ["outcome"] = compiled ? "passed" : "failed", ["diagnostics"] = diagnostics.DeepClone() };

        JsonObject scenarios;
        if (compiled)
        {
            var run = await RunScenariosAsync(context, arguments, cancellationToken);
            if (run is null)
            {
                return Unfinished();
            }

            scenarios = new JsonObject
            {
                ["outcome"] = run["status"]?.GetValue<string>() == "succeeded" ? "passed" : "failed",
                ["request"] = run["request"]?.DeepClone(),
                ["diagnostics"] = run["diagnostics"]?.DeepClone() ?? new JsonArray(),
                ["items"] = run["scenarios"]?.DeepClone() ?? new JsonArray(),
            };
        }
        else
        {
            scenarios = new JsonObject { ["outcome"] = "notRun", ["request"] = null, ["diagnostics"] = new JsonArray(), ["items"] = new JsonArray() };
        }

        var checks = new JsonObject { ["compile"] = compile, ["scenarios"] = scenarios }.ToJsonString();
        return compiled && scenarios["outcome"]!.GetValue<string>() == "passed"
            ? new AuthoringStepResult("reviewing", "passed", Checks: checks)
            : new AuthoringStepResult("fixing", "failed", Checks: checks);
    }

    // Starts a run with no names, so it covers every scenario, and asks for its status until it ends. A run starts again when the step does.
    private async Task<JsonObject?> RunScenariosAsync(AuthoringStepContext context, JsonObject arguments, CancellationToken cancellationToken)
    {
        var started = await context.Mcp.CallToolAsync("run_scenarios", (JsonObject)arguments.DeepClone(), cancellationToken);
        if (started.IsError || started.StructuredContent?["request"] is not JsonValue request || !request.TryGetValue<string>(out var requestId))
        {
            return null;
        }

        var poll = (JsonObject)arguments.DeepClone();
        poll["request"] = requestId;
        for (var polls = 0; polls < options.MaxPolls; polls++)
        {
            await Task.Delay(options.PollInterval, time, cancellationToken);
            var answer = await context.Mcp.CallToolAsync("run_scenarios", (JsonObject)poll.DeepClone(), cancellationToken);
            if (answer.IsError || answer.StructuredContent is not JsonObject run)
            {
                return null;
            }

            if (run["status"]?.GetValue<string>() is "succeeded" or "failed")
            {
                return run;
            }
        }

        return null;
    }

    private static AuthoringStepResult Unfinished() => new("failed", "unfinished");
}
