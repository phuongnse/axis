namespace Axis.Worker;

/// <summary>
/// Records that the start check of the releases in use failed. The background service sets it
/// and stops the host. The entry point reads it after the host stops, to return a non-zero exit code.
/// </summary>
internal sealed class StartupCheckState
{
    public bool Failed { get; set; }
}
