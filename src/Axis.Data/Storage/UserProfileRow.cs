namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.user_profiles</c>: what the last sign-in of a user told Axis about them. Each sign-in writes it,
/// and nothing else does.
/// </summary>
public sealed class UserProfileRow
{
    /// <summary>The user id from the identity provider's user id claim.</summary>
    public required string UserId { get; init; }

    /// <summary>The display name of the last sign-in, which is the user id when the token had none.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The email of the last sign-in, or null when the ID token had none.</summary>
    public string? Email { get; init; }

    /// <summary>The locale of the last sign-in, or null when the ID token had none.</summary>
    public string? Locale { get; init; }

    /// <summary>The time of the first sign-in.</summary>
    public required DateTimeOffset FirstSignedInAt { get; init; }

    /// <summary>The time of the last sign-in.</summary>
    public required DateTimeOffset LastSignedInAt { get; init; }
}
