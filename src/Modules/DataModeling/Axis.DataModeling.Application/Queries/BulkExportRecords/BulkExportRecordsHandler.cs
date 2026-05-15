using System.Text;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Queries.BulkExportRecords;

public sealed class BulkExportRecordsHandler(
    IDataModelRepository modelRepo,
    IDataRecordRepository recordRepo)
    : IQueryHandler<BulkExportRecordsQuery, Result<string>>
{
    public async Task<Result<string>> Handle(BulkExportRecordsQuery query, CancellationToken cancellationToken)
    {
        if (query.RecordIds.Count == 0)
            return Result.Failure<string>("validation_error", "No records selected for export.");

        DataModel? model = await modelRepo.GetByIdAsync(query.ModelId, query.OrganizationId, cancellationToken);
        if (model is null)
            return Result.Failure<string>(ErrorCodes.NotFound, "Model not found.");

        List<DataRecord> records = [];
        foreach (Guid id in query.RecordIds)
        {
            DataRecord? record = await recordRepo.GetByIdAsync(id, query.ModelId, query.OrganizationId, cancellationToken);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        StringBuilder csv = new StringBuilder();

        // Header
        var customFields = model.Fields.Where(f => !f.IsSystem).OrderBy(f => f.DisplayOrder).ToList();
        var headers = new List<string> { "ID", "Created At", "Updated At" };
        headers.AddRange(customFields.Select(f => f.Name));
        csv.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

        // Rows
        foreach (var record in records)
        {
            var row = new List<string>
            {
                record.Id.ToString(),
                record.CreatedAt.ToString("O"),
                record.UpdatedAt.ToString("O")
            };

            foreach (var field in customFields)
            {
                if (record.Data.TryGetValue(field.Name, out var value) && value is not null)
                {
                    row.Add(EscapeCsv(value.ToString() ?? ""));
                }
                else
                {
                    row.Add("");
                }
            }

            csv.AppendLine(string.Join(",", row));
        }

        return csv.ToString();
    }

    private static string EscapeCsv(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}
