namespace Axis.ControlPlane.SignUp;

/// <summary>
/// A row of <c>control_plane.sign_up_challenges</c>: one proof-of-work solution that a sign-up used, see
/// <c>docs/reference/sign-up.md#bots-and-abuse</c>. A solution is accepted once, so a second sign-up that carries it finds its row and is refused.
/// The <c>expireSignUps</c> job deletes the row once <see cref="ExpiresAt"/> has passed, as the solution is refused after that anyway.
/// </summary>
public sealed class SignUpChallengeRow
{
    /// <summary>The 32 bytes of the SHA-256 challenge that the solution solved.</summary>
    public byte[] Challenge { get; set; } = [];

    /// <summary>When the challenge expires: the <c>expires</c> of its salt.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
