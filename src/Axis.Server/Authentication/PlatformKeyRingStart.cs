namespace Axis.Server.Authentication;

/// <summary>
/// Decrypts every stored data protection key in <see cref="StartingAsync"/>, so a key that does not decrypt, for
/// example because this replica has another <c>Platform:SecretKey</c>, stops the start with an error that names the
/// setting. It is registered after the platform migration and before startup activation.
/// </summary>
internal sealed class PlatformKeyRingStart(PlatformKeyRing ring) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        ring.GetAllElements();
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
