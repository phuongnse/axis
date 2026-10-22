using Xunit.Sdk;

namespace Axis.Integration.Tests;

public sealed class PollingTests
{
    [Fact]
    public async Task It_returns_once_the_condition_holds()
    {
        var calls = 0;

        await Polling.WaitUntilAsync(() => ++calls >= 3, "The condition did not hold.");

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task It_fails_with_the_caller_message_when_the_deadline_passes()
    {
        var exception = await Assert.ThrowsAnyAsync<XunitException>(
            () => Polling.WaitUntilAsync(() => false, "The entry was not logged.", TimeSpan.FromMilliseconds(200)));

        Assert.Contains("The entry was not logged.", exception.Message, StringComparison.Ordinal);
    }
}
