using System.Text;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Entities;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Queries.ExportRecordsCsv;

/// <summary>US-046: Streams records through the repository and builds a CSV string with proper escaping.</summary>
public sealed class ExportRecordsCsvHandler(
    IDataModelRepository modelRepo,
    IDataRecordRepository recordRepo)
    : IQueryHandler<ExportRecordsCsvQuery, Result<CsvExportDto>>
{
    public async Task<Result<CsvExportDto>> Handle(
        ExportRecordsCsvQuery query, CancellationToken cancellationToken)
    {
        DataModel? model = await modelRepo.GetByIdAsync(query.ModelId, query.OrganizationId, cancellationToken);
        if (model is null)
            return Result.Failure<CsvExportDto>(ErrorCodes.NotFound, "Model not found.");

        IReadOnlyList<FieldDefinition> fields = model.Fields
            .OrderBy(f => f.IsSystem ? 0 : 1)
            .ThenBy(f => f.DisplayOrder)
            .ToList()
            .AsReadOnly();

        StringBuilder sb = new();

        // Header row
        sb.AppendLine(string.Join(",", fields.Select(f => Escape(f.Label))));

        // Data rows — streamed to avoid loading all records at once
        await foreach (DataRecord record in recordRepo.GetAllForExportAsync(
            query.ModelId, query.OrganizationId,
            query.Search, query.Filters,
            query.SortBy, query.SortDir,
            cancellationToken))
        {
            sb.AppendLine(string.Join(",", fields.Select(f => Escape(ResolveValue(record, f)))));
        }

        string slug = model.Name.ToLowerInvariant().Replace(' ', '-');
        string date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        string fileName = $"{slug}-records-{date}.csv";

        return Result.Success(new CsvExportDto(fileName, sb.ToString()));
    }

    private static string ResolveValue(DataRecord record, FieldDefinition field) =>
        field.Name switch
        {
            "id"         => record.Id.ToString(),
            "created_at" => record.CreatedAt.ToString("O"),
            "updated_at" => record.UpdatedAt.ToString("O"),
            _            => record.Data.TryGetValue(field.Name, out object? val)
                                ? val?.ToString() ?? string.Empty
                                : string.Empty
        };

    /// <summary>RFC 4180 CSV escaping: wrap in double-quotes and double any internal double-quotes.</summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
