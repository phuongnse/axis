namespace Axis.Server.Health;

/// <summary>
/// Whether startup activation has finished for every tenant. It starts finished when no application
/// folder is configured, because there is nothing to wait for.
/// </summary>
internal sealed class StartupActivationStatus(bool finished)
{
    private volatile bool _finished = finished;

    public bool IsFinished => _finished;

    public void MarkFinished() => _finished = true;
}
