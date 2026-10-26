namespace Axis.Data.Files;

/// <summary>The details of a file that <see cref="FileStore.InsertPendingAsync"/> stores. The file is new and has no owner record.</summary>
public sealed record PendingFile(
    Guid Id,
    Guid ApplicationId,
    Guid EntityId,
    string FieldPath,
    string Name,
    string MediaType,
    long Size,
    byte[] Sha256,
    string UploadedBy,
    DateTimeOffset UploadedAt);
