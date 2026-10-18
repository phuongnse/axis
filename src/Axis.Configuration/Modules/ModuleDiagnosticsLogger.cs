using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Configuration.Modules;

/// <summary>Logs the problem of each module version that was left out of the catalog as a warning, once at start.</summary>
internal sealed partial class ModuleDiagnosticsLogger(ModuleCatalog catalog, ILogger<ModuleDiagnosticsLogger> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var diagnostic in catalog.Diagnostics)
        {
            LogDiagnostic(diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.Message);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(LogLevel.Warning, "{Code} in module {File} {Path}: {Message} The module version is not loaded.")]
    private partial void LogDiagnostic(string code, string file, string path, string message);
}
