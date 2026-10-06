namespace Axis.Data.Records;

/// <summary>How a record delete ended.</summary>
public enum RecordDeleteOutcome
{
    /// <summary>The record was deleted.</summary>
    Deleted,

    /// <summary>No record has the id.</summary>
    NotFound,

    /// <summary>Another record references the record through a foreign key, so it was kept.</summary>
    Referenced,
}
