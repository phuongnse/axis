namespace Axis.Agent.Tests;

/// <summary>
/// The review runs on <c>Ai:ReviewBinding</c> when it is set, and otherwise on the request's binding or the default binding, see
/// <c>docs/reference/authoring-requests.md#states</c>.
/// </summary>
public sealed class ReviewingStepTests
{
    private static AiSettings Settings(string? reviewBinding) => new()
    {
        Bindings = new Dictionary<string, ModelBinding>(),
        DefaultBinding = "main",
        ReviewBinding = reviewBinding,
    };

    private static AuthoringRequestToWork Request(string? binding) =>
        new(Guid.NewGuid(), "reviewing", DateTimeOffset.UtcNow, Guid.NewGuid(), "Add a review", "A plan", binding, [], Guid.NewGuid(), null, 0, null);

    [Fact]
    public void The_review_binding_is_used_when_it_is_set()
    {
        Assert.Equal("review", ReviewingStep.BindingFor(Settings("review"), Request("own")));
    }

    [Fact]
    public void Without_a_review_binding_the_review_uses_the_binding_of_the_request()
    {
        Assert.Equal("own", ReviewingStep.BindingFor(Settings(null), Request("own")));
    }

    [Fact]
    public void Without_a_review_binding_or_a_request_binding_the_review_uses_the_default_binding()
    {
        Assert.Equal("main", ReviewingStep.BindingFor(Settings(null), Request(null)));
    }
}
