using Axis.Configuration.Model;
using Axis.Configuration.Resources;
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
                new ProcessGraphStep("fulfil", "operation", null, null, null, [new ProcessGraphTransition("done", null, null, null, null)]),
                new ProcessGraphStep("done", "end", null, null, null, []),
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
                new WaitEventStepModel("wait", "OrderPlaced", null, new WaitTimeoutModel(TimeSpan.FromDays(2), "P2D", "expired"), "done"),
                new EndStepModel("done"),
                new EndStepModel("expired"),
            ],
        };

        var graph = ProcessInspectionEndpoints.Graph(model);

        Assert.Equal(
            [
                new ProcessGraphStep(
                    "wait",
                    "waitEvent",
                    null,
                    "OrderPlaced",
                    null,
                    [new ProcessGraphTransition("done", null, null, null, null), new ProcessGraphTransition("expired", "timeout", null, null, "P2D")]),
                new ProcessGraphStep("done", "end", null, null, null, []),
                new ProcessGraphStep("expired", "end", null, null, null, []),
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

        Assert.Equal([new ProcessGraphTransition("done", null, null, null, null)], graph.Steps[0].Transitions);
    }

    [Fact]
    public void A_step_that_is_not_a_task_gives_its_own_label_key()
    {
        var model = new ProcessModel
        {
            Id = Guid.Parse("77777777-7777-4777-8777-777777777705"),
            Name = "OrderReview",
            File = "processes/order-review.json",
            Start = "pause",
            Steps =
            [
                new TimerStepModel("pause", TimeSpan.FromHours(1), null, "wait") { Label = new TextReference("step.pause") },
                new WaitEventStepModel("wait", "OrderPlaced", null, null, "done") { Label = new TextReference("step.wait") },
                new EndStepModel("done") { Label = new TextReference("step.done") },
            ],
        };

        var graph = ProcessInspectionEndpoints.Graph(model);

        Assert.Equal(["step.pause", "step.wait", "step.done"], graph.Steps.Select(step => step.LabelKey));
        Assert.Null(graph.Steps[1].EventLabelKey);
    }

    // The records hold lists, which a record compares by reference, so the steps are compared by their parts.
    private sealed class GraphStepComparer : IEqualityComparer<ProcessGraphStep>
    {
        public bool Equals(ProcessGraphStep? x, ProcessGraphStep? y) =>
            x is not null && y is not null
            && (x.Name, x.Type, x.LabelKey, x.Event, x.EventLabelKey) == (y.Name, y.Type, y.LabelKey, y.Event, y.EventLabelKey)
            && x.Transitions.SequenceEqual(y.Transitions);

        public int GetHashCode(ProcessGraphStep obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }
}
