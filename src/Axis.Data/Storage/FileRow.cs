namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.files</c>: the details of one uploaded file of a file field. The bytes are in
/// <c>axis.file_contents</c>, under the same id. A file starts <c>pending</c> when it is uploaded.
/// </summary>
public sealed class FileRow
{
    public required Guid Id { get; init; }

    public required Guid ApplicationId { get; init; }

    /// <summary>The <c>id</c> of the entity that declares the file field, which is the child entity for a field of a child row.</summary>
    public required Guid EntityId { get; init; }

    /// <summary>The declared field path: the field name, or <c>collection/field</c> for a field of a child entity.</summary>
    public required string FieldPath { get; init; }

    /// <summary>The record that holds the file, or null until the file is attached.</summary>
    public Guid? OwnerRecordId { get; init; }

    public required string Name { get; init; }

    public required string MediaType { get; init; }

    public required long Size { get; init; }

    /// <summary>The SHA-256 hash of the bytes.</summary>
    public required byte[] Sha256 { get; init; }

    /// <summary>Where the bytes are kept. It is <c>database</c> for now.</summary>
    public required string StorageKind { get; init; }

    /// <summary>One of <c>pending</c>, <c>attached</c> and <c>deleted</c>.</summary>
    public required string State { get; init; }

    public required string UploadedBy { get; init; }

    public required DateTimeOffset UploadedAt { get; init; }
}
