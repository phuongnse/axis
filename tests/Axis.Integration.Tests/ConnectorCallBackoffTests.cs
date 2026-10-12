using Axis.Configuration.Model;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

public sealed class ConnectorCallBackoffTests
{
    [Theory]
    [InlineData(30, 1, 30)]
    [InlineData(30, 2, 60)]
    [InlineData(30, 3, 120)]
    [InlineData(30, 10, 3600)]
    [InlineData(1800, 2, 3600)]
    [InlineData(2400, 2, 3600)]
    [InlineData(86400, 1, 3600)]
    public void Backoff_doubles_after_each_attempt_up_to_an_hour(int backoffSeconds, int attempts, int expectedSeconds)
    {
        var policy = new RetryPolicyModel(10, TimeSpan.FromSeconds(backoffSeconds));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ConnectorCallHandler.Backoff(policy, attempts));
    }
}
