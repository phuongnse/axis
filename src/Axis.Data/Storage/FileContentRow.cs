namespace Axis.Data.Storage;

/// <summary>A row of <c>axis.file_contents</c>: the bytes of the file of <c>axis.files</c> with the same id.</summary>
public sealed class FileContentRow
{
    public required Guid FileId { get; init; }

    public required byte[] Content { get; init; }
}
