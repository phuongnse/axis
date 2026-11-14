using System.Text.Json.Nodes;

namespace Axis.Agent;

/// <summary>
/// The work of the <c>checking</c> state, see <c>docs/reference/authoring-requests.md#states</c>. It makes no model call. It compiles the request's draft
/// with <c>validate_draft</c> and, when no diagnostic is an error, tests each package that the draft changed and still holds with <c>test_package</c>,
/// then runs every scenario of the application and its modules on it with <c>run_scenarios</c>. It polls each run until it ends. It stores
/// <c>{ compile, packages, scenarios }</c> on its step and moves the request to <c>reviewing</c> when the compile, the package tests and the scenarios
/// passed, or to <c>fixing</c> when one failed. A run that has not ended within <see cref="CheckingOptions.MaxPolls"/> polls, a tool error, a request
/// without a draft and an application the author can no longer list stop the request as <c>failed</c> with the outcome <c>unfinished</c>, see
/// <c>docs/reference/authoring-requests.md#budgets</c>.
/// </summary>
internal sealed class CheckingStep(CheckingOptions options, TimeProvider time) : IAuthoringStep
{
    private const string PackagesFolder = "packages/";

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

        JsonObject packages;
        JsonObject scenarios;
        if (compiled)
        {
            var changed = await ChangedPackagesAsync(context, arguments, cancellationToken);
            if (changed is null)
            {
                return Unfinished();
            }

            var tested = await TestPackagesAsync(context, arguments, changed, cancellationToken);
            if (tested is null)
            {
                return Unfinished();
            }

            packages = tested;

            var run = await PollRunAsync(context, "run_scenarios", arguments, cancellationToken);
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
            packages = NotRunPackages();
            scenarios = new JsonObject { ["outcome"] = "notRun", ["request"] = null, ["diagnostics"] = new JsonArray(), ["items"] = new JsonArray() };
        }

        var checks = new JsonObject { ["compile"] = compile, ["packages"] = packages, ["scenarios"] = scenarios }.ToJsonString();
        return compiled && scenarios["outcome"]!.GetValue<string>() == "passed" && packages["outcome"]!.GetValue<string>() != "failed"
            ? new AuthoringStepResult("reviewing", "passed", Checks: checks)
            : new AuthoringStepResult("fixing", "failed", Checks: checks);
    }

    // The ids of the packages under packages/<id>/ that the draft's diff lists, read from every page. Null when a page is a tool error.
    private static async Task<SortedSet<string>?> ChangedPackagesAsync(AuthoringStepContext context, JsonObject arguments, CancellationToken cancellationToken)
    {
        var changed = new SortedSet<string>(StringComparer.Ordinal);
        var page = (JsonObject)arguments.DeepClone();
        while (true)
        {
            var diff = await context.Mcp.CallToolAsync("diff_draft", (JsonObject)page.DeepClone(), cancellationToken);
            if (diff.IsError || diff.StructuredContent is not JsonObject answer)
            {
                return null;
            }

            foreach (var file in (answer["resources"] as JsonArray ?? []).OfType<JsonObject>().Select(resource => resource["file"]?.GetValue<string>()))
            {
                if (file is null || !file.StartsWith(PackagesFolder, StringComparison.Ordinal))
                {
                    continue;
                }

                var end = file.IndexOf('/', PackagesFolder.Length);
                if (end > PackagesFolder.Length)
                {
                    changed.Add(file[PackagesFolder.Length..end]);
                }
            }

            if (answer["cursor"] is not JsonValue cursor || !cursor.TryGetValue<string>(out var next))
            {
                return changed;
            }

            page["cursor"] = next;
        }
    }

    // Tests, one after the other, each changed package that the draft still holds. A package whose files were all deleted has nothing to test, and
    // test_package would refuse it. Null when a call is a tool error or a test does not end.
    private async Task<JsonObject?> TestPackagesAsync(
        AuthoringStepContext context, JsonObject arguments, SortedSet<string> changed, CancellationToken cancellationToken)
    {
        if (changed.Count == 0)
        {
            return NotRunPackages();
        }

        var listed = await context.Mcp.CallToolAsync("list_package_files", (JsonObject)arguments.DeepClone(), cancellationToken);
        if (listed.IsError || listed.StructuredContent?["items"] is not JsonArray files)
        {
            return null;
        }

        var held = files.OfType<JsonObject>().Select(file => file["package"]?.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var items = new JsonArray();
        foreach (var package in changed.Where(held.Contains))
        {
            var input = (JsonObject)arguments.DeepClone();
            input["package"] = package;
            var run = await PollRunAsync(context, "test_package", input, cancellationToken);
            if (run is null)
            {
                return null;
            }

            items.Add(new JsonObject
            {
                ["package"] = package,
                ["request"] = run["request"]?.DeepClone(),
                ["status"] = run["status"]?.DeepClone(),
                ["diagnostics"] = run["diagnostics"]?.DeepClone() ?? new JsonArray(),
                ["tests"] = run["tests"]?.DeepClone() ?? new JsonArray(),
            });
        }

        if (items.Count == 0)
        {
            return NotRunPackages();
        }

        var passed = items.All(item => item!["status"]?.GetValue<string>() == "succeeded");
        return new JsonObject { ["outcome"] = passed ? "passed" : "failed", ["items"] = items };
    }

    private static JsonObject NotRunPackages() => new() { ["outcome"] = "notRun", ["items"] = new JsonArray() };

    // Starts a run of the tool, which takes no names for the scenarios so it covers every one, and asks for its status until it ends. A run starts again when the step does.
    private async Task<JsonObject?> PollRunAsync(AuthoringStepContext context, string tool, JsonObject arguments, CancellationToken cancellationToken)
    {
        var started = await context.Mcp.CallToolAsync(tool, (JsonObject)arguments.DeepClone(), cancellationToken);
        if (started.IsError || started.StructuredContent?["request"] is not JsonValue request || !request.TryGetValue<string>(out var requestId))
        {
            return null;
        }

        var poll = (JsonObject)arguments.DeepClone();
        poll["request"] = requestId;
        for (var polls = 0; polls < options.MaxPolls; polls++)
        {
            await Task.Delay(options.PollInterval, time, cancellationToken);
            var answer = await context.Mcp.CallToolAsync(tool, (JsonObject)poll.DeepClone(), cancellationToken);
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
