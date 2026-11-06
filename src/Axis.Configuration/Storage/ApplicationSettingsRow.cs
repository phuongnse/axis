namespace Axis.Configuration.Storage;

/// <summary>
/// The settings of one application that live in the tenant database and in no resource file, so
/// editing the application's files cannot change them. A missing row means every setting has its default.
/// </summary>
public sealed class ApplicationSettingsRow
{
    /// <summary>The <c>id</c> of the application manifest.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary>Whether AI may accept its own drafts. Off by default.</summary>
    public bool AutoAccept { get; init; }
}
