namespace Axis.Configuration.Storage;

/// <summary>
/// One resource file of a release: its path relative to the application folder with <c>/</c>
/// separators, and its content as canonical JSON (RFC 8785).
/// </summary>
public sealed class ReleaseResource
{
    public required Guid ReleaseId { get; init; }

    public required string Path { get; init; }

    public required string Content { get; init; }
}
