using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Configuration.Packages;

/// <summary>Logs the problem of each extension package that was left out of the catalog as a warning, once at start.</summary>
internal sealed partial class PackageDiagnosticsLogger(PackageCatalog catalog, ILogger<PackageDiagnosticsLogger> logger) : IHostedService
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

    [LoggerMessage(LogLevel.Warning, "{Code} in extension package {File} {Path}: {Message} The package is not loaded.")]
    private partial void LogDiagnostic(string code, string file, string path, string message);
}
