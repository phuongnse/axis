namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.record_imports</c>: one upload of records for an entity, with the person who
/// uploaded it, the file until it is no longer needed, and the result of its dry run. The row is written
/// by the record API and by the worker, so it holds the uploader's id, name and roles for the checks the
/// worker runs on their behalf.
/// </summary>
public sealed class RecordImportRow
{
    public required Guid Id { get; init; }

    public required string TenantId { get; init; }

    public required Guid ApplicationId { get; init; }

    public required Guid EntityId { get; init; }

    public required string UploaderId { get; init; }

    public required string UploaderName { get; init; }

    public required string[] UploaderRoles { get; init; }

    /// <summary>The unique field that matches rows to existing records, or null when every row creates a record.</summary>
    public string? Key { get; init; }

    /// <summary><c>pending</c>, <c>checked</c>, <c>failed</c> or <c>committed</c>.</summary>
    public required string State { get; init; }

    /// <summary><c>csv</c> or <c>xlsx</c>, which picks the reader that reads the file.</summary>
    public required string Format { get; init; }

    /// <summary>The uploaded file, or null once a failed dry run has cleared it.</summary>
    public byte[]? File { get; init; }

    /// <summary>The errors of the dry run as a JSON object of JSON Pointer to messages, or null when there are none.</summary>
    public string? Errors { get; init; }

    /// <summary>The work item that runs the dry run of the import, so the worker finds its import from its item.</summary>
    public Guid? WorkItemId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
