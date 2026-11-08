using Axis.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axis.Agent.Tests;

public sealed class AuthoringStepsTests
{
    private sealed class FakeStep(string state, string kind = "step") : IAuthoringStep
    {
        public string State { get; } = state;

        public string Kind { get; } = kind;

        public Task<AuthoringStepResult> RunAsync(AuthoringStepContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new AuthoringStepResult("ready"));
    }

    [Fact]
    public void Steps_for_different_states_that_need_work_are_accepted_and_found_by_state()
    {
        var working = new FakeStep("working");
        var checking = new FakeStep("checking");

        var steps = new AuthoringSteps([working, checking]);

        Assert.True(steps.Any);
        Assert.Equal(["working", "checking"], steps.States);
        Assert.Same(working, steps.Find("working"));
        Assert.True(steps.Has("checking"));
        Assert.False(steps.Has("planning"));
        Assert.Null(steps.Find("planning"));
    }

    [Fact]
    public void No_step_is_accepted_and_claims_no_state()
    {
        var steps = new AuthoringSteps([]);

        Assert.False(steps.Any);
        Assert.Empty(steps.States);
    }

    [Fact]
    public void Two_steps_for_one_state_are_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new AuthoringSteps([new FakeStep("working", "first"), new FakeStep("working", "second")]));

        Assert.Contains("'working'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("awaitingApproval")]
    [InlineData("ready")]
    [InlineData("accepted")]
    [InlineData("dropped")]
    [InlineData("failed")]
    [InlineData("unknown")]
    public void A_step_for_a_state_that_needs_no_work_is_refused(string state)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new AuthoringSteps([new FakeStep(state)]));

        Assert.Contains($"'{state}'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("working", "working", "Two authoring steps")]
    [InlineData("awaitingApproval", "ready", "needs no work")]
    public void A_bad_set_of_steps_stops_the_agent_host_from_starting(string first, string second, string message)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAuthoringStep>(new FakeStep(first, "first"));
        services.AddSingleton<IAuthoringStep>(new FakeStep(second, "second"));
        services.AddAxisAgent(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Bindings:main:Kind"] = "anthropic",
                ["Ai:Bindings:main:Model"] = "model-1",
                ["Ai:DefaultBinding"] = "main",
                ["Tenants:a:Hosts:0"] = "a.example.test",
                ["Tenants:a:ConnectionString"] = "Host=db.example.test;Database=axis;Username=axis;Password=secret",
            }).Build(),
            new TestEnvironment());
        using var provider = services.BuildServiceProvider();

        // The host builds its hosted services when it starts.
        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetServices<IHostedService>().ToList());

        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }
}
