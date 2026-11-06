namespace Axis.Agent;

/// <summary>The agent host's background service. It has no request loop yet, so it only says whether AI authoring is on.</summary>
internal sealed partial class AgentService(AiSettings settings, ILogger<AgentService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (settings.DefaultBinding is null)
        {
            LogAuthoringOff();
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(LogLevel.Information, "AI authoring is off, because Ai:DefaultBinding has no value.")]
    private partial void LogAuthoringOff();
}
