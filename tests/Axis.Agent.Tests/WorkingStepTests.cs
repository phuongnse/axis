using Axis.Agent;

namespace Axis.Agent.Tests;

public sealed class WorkingStepTests
{
    private static AuthoringRequestToWork Request(IReadOnlyList<string>? changeRequests) =>
        new(Guid.NewGuid(), "working", DateTimeOffset.UtcNow, Guid.NewGuid(), "Add a review", "A plan", null, [], Guid.NewGuid(), null, 0, null, 0, null, changeRequests);

    [Fact]
    public void The_first_message_holds_every_change_request_comment_oldest_first()
    {
        var message = WorkingStep.FirstMessage(Request(["Rename the form", "Add a second scenario"]));

        Assert.Contains("Add a review", message, StringComparison.Ordinal);
        Assert.Contains("A plan", message, StringComparison.Ordinal);
        Assert.True(message.IndexOf("Rename the form", StringComparison.Ordinal) < message.IndexOf("Add a second scenario", StringComparison.Ordinal));
        Assert.Contains("Change the draft as these comments ask.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_request_with_no_change_request_has_no_change_part()
    {
        var message = WorkingStep.FirstMessage(Request(null));

        Assert.DoesNotContain("asked for changes", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Change the draft", message, StringComparison.Ordinal);
    }
}
