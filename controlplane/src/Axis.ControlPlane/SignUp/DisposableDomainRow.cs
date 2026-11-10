namespace Axis.ControlPlane.SignUp;

/// <summary>
/// A row of <c>control_plane.disposable_domains</c>: one domain of a disposable mailbox service that sign-up refuses, see
/// <c>docs/reference/sign-up.md#bots-and-abuse</c>. <see cref="Domain"/> is lower case.
/// </summary>
public sealed class DisposableDomainRow
{
    public string Domain { get; set; } = "";
}
