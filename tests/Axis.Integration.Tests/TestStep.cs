using Axis.Agent;

namespace Axis.Integration.Tests;

/// <summary>A step of the agent host for the state <paramref name="state"/> that runs <paramref name="body"/> and moves the request to <paramref name="nextState"/> with the outcome <c>done</c>.</summary>
internal sealed class TestStep(string state, string kind, string nextState, Func<AuthoringStepContext, CancellationToken, Task>? body = null) : IAuthoringStep
{
    public string State { get; } = state;

    public string Kind { get; } = kind;

    public async Task<AuthoringStepResult> RunAsync(AuthoringStepContext context, CancellationToken cancellationToken)
    {
        if (body is not null)
        {
            await body(context, cancellationToken);
        }

        return new AuthoringStepResult(nextState, "done");
    }
}
