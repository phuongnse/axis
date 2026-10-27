using Axis.Configuration.Model;
using Axis.Server.Processes;

namespace Axis.Server.Tests;

/// <summary>The graph the instance detail shows for a process: each step has a type and its transitions.</summary>
public sealed class ProcessGraphTests
{
    [Fact]
    public void A_sub_process_step_is_an_operation_step_with_one_transition_to_its_next_step()
    {
        var model = new ProcessModel
        {
            Id = Guid.Parse("77777777-7777-4777-8777-777777777701"),
            Name = "OrderReview",
            File = "processes/order-review.json",
            Start = "fulfil",
            Steps =
            [
                new SubProcessStepModel("fulfil", Guid.Parse("77777777-7777-4777-8777-777777777702"), "Fulfilment", null, [], "done"),
                new EndStepModel("done"),
            ],
        };

        var graph = ProcessInspectionEndpoints.Graph(model);

        Assert.Equal("fulfil", graph.Start);
        Assert.Equal(
            [
                new ProcessGraphStep("fulfil", "operation", null, [new ProcessGraphTransition("done", null)]),
                new ProcessGraphStep("done", "end", null, []),
            ],
            graph.Steps,
            new GraphStepComparer());
    }

    [Fact]
    public void A_wait_event_step_has_a_transition_to_its_next_step_and_one_marked_timeout()
    {
        var model = new ProcessModel
        {
            Id = Guid.Parse("77777777-7777-4777-8777-777777777703"),
            Name = "OrderReview",
            File = "processes/order-review.json",
            Start = "wait",
            Steps =
            [
                new WaitEventStepModel("wait", "OrderPlaced", null, new WaitTimeoutModel(TimeSpan.FromDays(2), "expired"), "done"),
                new EndStepModel("done"),
                new EndStepModel("expired"),
            ],
        };

        var graph = ProcessInspectionEndpoints.Graph(model);

        Assert.Equal(
            [
                new ProcessGraphStep("wait", "waitEvent", null, [new ProcessGraphTransition("done", null), new ProcessGraphTransition("expired", "timeout")]),
                new ProcessGraphStep("done", "end", null, []),
                new ProcessGraphStep("expired", "end", null, []),
            ],
            graph.Steps,
            new GraphStepComparer());
    }

    [Fact]
    public void A_wait_event_step_without_a_timeout_has_one_transition()
    {
        var model = new ProcessModel
        {
            Id = Guid.Parse("77777777-7777-4777-8777-777777777704"),
            Name = "OrderReview",
            File = "processes/order-review.json",
            Start = "wait",
            Steps = [new WaitEventStepModel("wait", "OrderPlaced", null, null, "done"), new EndStepModel("done")],
        };

        var graph = ProcessInspectionEndpoints.Graph(model);

        Assert.Equal([new ProcessGraphTransition("done", null)], graph.Steps[0].Transitions);
    }

    // The records hold lists, which a record compares by reference, so the steps are compared by their parts.
    private sealed class GraphStepComparer : IEqualityComparer<ProcessGraphStep>
    {
        public bool Equals(ProcessGraphStep? x, ProcessGraphStep? y) =>
            x is not null && y is not null
            && (x.Name, x.Type, x.LabelKey) == (y.Name, y.Type, y.LabelKey)
            && x.Transitions.SequenceEqual(y.Transitions);

        public int GetHashCode(ProcessGraphStep obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }
}
