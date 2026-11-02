using Axis.Configuration.Model;
using Axis.Data.Records;

namespace Axis.Data.Tests;

public sealed class RecordLifecycleProtectedStateTests
{
    private static readonly LifecycleModel _lifecycle = new()
    {
        Field = "status",
        States =
        [
            new LifecycleStateModel("draft", true, true),
            new LifecycleStateModel("submitted", false, false),
            new LifecycleStateModel("approved", false, false, Protected: true),
            new LifecycleStateModel("onHold", false, false),
        ],
        Transitions = [],
    };

    [Fact]
    public void An_added_transition_into_a_protected_state_is_refused()
    {
        var added = Transition("skip", "onHold", "approved", added: true);

        Assert.True(RecordLifecycle.EntersProtectedState(_lifecycle, added));
    }

    [Fact]
    public void A_transition_of_the_module_into_a_protected_state_is_allowed()
    {
        var module = Transition("approve", "submitted", "approved", added: false);

        Assert.False(RecordLifecycle.EntersProtectedState(_lifecycle, module));
    }

    [Fact]
    public void An_added_transition_into_a_state_that_is_not_protected_is_allowed()
    {
        var added = Transition("hold", "submitted", "onHold", added: true);

        Assert.False(RecordLifecycle.EntersProtectedState(_lifecycle, added));
    }

    private static LifecycleTransitionModel Transition(string name, string from, string to, bool added) =>
        new() { Name = name, From = [from], To = to, Added = added };
}
