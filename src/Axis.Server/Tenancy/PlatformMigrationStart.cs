namespace Axis.Server.Tenancy;

/// <summary>
/// Runs <see cref="PlatformMigration"/> under <c>Tenancy:Source=configuration</c>. It does its work in
/// <see cref="StartingAsync"/>, like startup activation, and it is registered as the first hosted service. So the
/// migration runs before startup activation, and a platform database that cannot be reached stops the start before
/// any application is activated.
/// </summary>
internal sealed class PlatformMigrationStart(PlatformMigrationOptions options) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => PlatformMigration.ApplyAsync(options, cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
