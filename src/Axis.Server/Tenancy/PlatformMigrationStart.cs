namespace Axis.Server.Tenancy;

/// <summary>
/// Runs <see cref="PlatformMigration"/> at start under <c>Tenancy:Source=configuration</c>. It is registered as the
/// first hosted service, so it runs before startup activation.
/// </summary>
internal sealed class PlatformMigrationStart(PlatformMigrationOptions options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => PlatformMigration.ApplyAsync(options, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
