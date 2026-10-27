namespace Axis.Processes.Records;

/// <summary>The messages of the rows of an import that a record command refused without errors by pointer.</summary>
internal static class RecordImportRefusals
{
    /// <summary>
    /// The message of a refusal of a create, or of an update when <paramref name="update"/> is set. It never holds
    /// a value.
    /// </summary>
    public static string Message(RecordCommandResult result, bool update)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            RecordCommandOutcome.Denied => update
                ? "The uploader may not update records of this entity."
                : "The uploader may not create records of this entity.",
            RecordCommandOutcome.OutsideFilters => update
                ? "The record is outside the uploader's update filters."
                : "The record is outside the uploader's create filters.",
            RecordCommandOutcome.PendingFilter => "The uploader's record filters cannot be checked now.",
            RecordCommandOutcome.Conflict when result.Conflict == RecordConflict.Schema => "The stored schema does not match the active release.",
            _ => "The row could not be written.",
        };
    }
}
