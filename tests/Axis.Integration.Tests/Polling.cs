namespace Axis.Integration.Tests;

/// <summary>Waits for an effect another thread or process writes, such as a worker's row or log entry.</summary>
internal static class Polling
{
    public static Task WaitUntilAsync(Func<bool> condition, string failure, TimeSpan? timeout = null) =>
        WaitUntilAsync(() => Task.FromResult(condition()), failure, timeout);

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, string failure, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }
    }
}
