namespace Axis.DataModeling.Application.Commands.BulkDeleteRecords;

/// <summary>Summary returned after a bulk-delete operation.</summary>
public sealed record BulkDeleteResult(int Deleted, int NotFound);
