namespace Axis.Configuration.Storage;

/// <summary>
/// Who last wrote one file of an application folder in Studio, and the version the write left, see
/// <c>docs/reference/studio-resource-form.md#resource-activity</c>. There is one row for each application and file.
/// </summary>
public sealed class ResourceWriteRow
{
    /// <summary>The <c>id</c> of the application manifest.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary>The path of the file relative to the application folder.</summary>
    public required string File { get; init; }

    /// <summary>The SHA-256 of the file's bytes that the write left.</summary>
    public required string Version { get; init; }

    /// <summary>The id of the user who wrote the file.</summary>
    public required string UserId { get; init; }

    /// <summary>The name of the user when the file was written.</summary>
    public required string UserName { get; init; }

    /// <summary>When the file was written.</summary>
    public required DateTimeOffset WrittenAt { get; init; }
}
