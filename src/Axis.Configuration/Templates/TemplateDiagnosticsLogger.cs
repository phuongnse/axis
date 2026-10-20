using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Configuration.Templates;

/// <summary>Logs the problem of each template version that was left out of the catalog as a warning, once at start.</summary>
internal sealed partial class TemplateDiagnosticsLogger(TemplateCatalog catalog, ILogger<TemplateDiagnosticsLogger> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var problem in catalog.Problems)
        {
            LogProblem(problem.Folder, problem.Message);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(LogLevel.Warning, "Template {Folder}: {Message} The template version is not loaded.")]
    private partial void LogProblem(string folder, string message);
}
