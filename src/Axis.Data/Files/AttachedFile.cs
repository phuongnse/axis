namespace Axis.Data.Files;

/// <summary>
/// The details of an <c>attached</c> file that <see cref="FileStore.FindAttachedAsync"/> finds: the entity that
/// declares the field, the field path, the record that owns the file, and the stored name and media type.
/// </summary>
public sealed record AttachedFile(
    Guid Id,
    Guid EntityId,
    string FieldPath,
    Guid OwnerRecordId,
    string Name,
    string MediaType);
