namespace Axis.Configuration.Storage;

/// <summary>
/// One built file of an application package of a release: its path relative to the application
/// folder with <c>/</c> separators, such as <c>packages/&lt;id&gt;/&lt;file&gt;</c>, and its bytes.
/// </summary>
public sealed class ReleasePackageFile
{
    public required Guid ReleaseId { get; init; }

    public required string Path { get; init; }

    public required byte[] Content { get; init; }
}
