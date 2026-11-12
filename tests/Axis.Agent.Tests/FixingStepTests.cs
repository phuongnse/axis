using Axis.Agent;

namespace Axis.Agent.Tests;

public sealed class FixingStepTests
{
    private const string Checks = """
        {
          "compile": { "outcome": "failed", "diagnostics": [{ "code": "AX1", "severity": "error", "message": "the-broken-reference" }] },
          "scenarios": {
            "outcome": "failed",
            "items": [
              { "name": "Failing scenario", "outcome": "failed", "step": "expectInstance" },
              { "name": "Passing scenario", "outcome": "passed" }
            ]
          }
        }
        """;

    private static AuthoringRequestToWork Request(string? checks, string? findings) =>
        new(Guid.NewGuid(), "fixing", DateTimeOffset.UtcNow, Guid.NewGuid(), "Add a review", "A plan", null, [], Guid.NewGuid(), checks, 1, findings);

    [Fact]
    public void The_first_message_holds_the_request_the_plan_the_failures_and_the_findings_and_leaves_out_what_passed()
    {
        var message = FixingStep.FirstMessage(Request(Checks, "The draft adds no scenario."));

        Assert.Contains("Add a review", message, StringComparison.Ordinal);
        Assert.Contains("A plan", message, StringComparison.Ordinal);
        Assert.Contains("the-broken-reference", message, StringComparison.Ordinal);
        Assert.Contains("Failing scenario", message, StringComparison.Ordinal);
        Assert.Contains("The draft adds no scenario.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Passing scenario", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_passed_compile_lists_no_diagnostics_and_a_request_without_findings_has_no_findings_part()
    {
        var checks = """{"compile":{"outcome":"passed","diagnostics":[{"code":"AX2","severity":"warning","message":"only-a-warning"}]},"scenarios":{"outcome":"passed","items":[{"name":"Passing scenario","outcome":"passed"}]}}""";

        var message = FixingStep.FirstMessage(Request(checks, null));

        Assert.DoesNotContain("only-a-warning", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Passing scenario", message, StringComparison.Ordinal);
        Assert.DoesNotContain("The review findings", message, StringComparison.Ordinal);
        Assert.Contains("What failed in the last check:\nnone", message, StringComparison.Ordinal);
    }
}
