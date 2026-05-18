namespace Axis.DataModeling.Application.Queries.ExportRecordsCsv;

/// <summary>Carries the CSV file name and content for the HTTP response.</summary>
public sealed record CsvExportDto(string FileName, string Content);
