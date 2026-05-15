using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;

namespace Axis.DataModeling.Infrastructure.Repositories;

internal sealed class DataRecordRepository(DataModelingDbContext context) : IDataRecordRepository
{
    private static readonly Regex SafeFieldRegex = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    public async Task AddAsync(DataRecord record, CancellationToken ct = default)
        => await context.DataRecords.AddAsync(record, ct);

    public async Task<DataRecord?> GetByIdAsync(Guid id, Guid modelId, Guid organizationId, CancellationToken ct = default)
        => await context.DataRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.ModelId == modelId && r.OrganizationId == organizationId, ct);

    public async Task<IReadOnlyList<DataRecord>> GetAllAsync(Guid modelId, Guid organizationId, CancellationToken ct = default)
        => await context.DataRecords
            .Where(r => r.ModelId == modelId && r.OrganizationId == organizationId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<DataRecord> Records, int TotalCount)> GetPagedAsync(
        Guid modelId, Guid organizationId,
        int page, int pageSize,
        string? search,
        string[]? filters = null,
        string? sortBy = null,
        bool? sortDesc = null,
        CancellationToken ct = default)
    {
        bool hasSearch = !string.IsNullOrWhiteSpace(search);
        bool hasFilters = filters is not null && filters.Length > 0;
        bool hasCustomSort = !string.IsNullOrWhiteSpace(sortBy);

        if (!hasSearch && !hasFilters && !hasCustomSort)
        {
            IQueryable<DataRecord> baseQuery = context.DataRecords
                .Where(r => r.ModelId == modelId && r.OrganizationId == organizationId);

            int totalCount = await baseQuery.CountAsync(ct);
            List<DataRecord> defaultRecords = await baseQuery
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return (defaultRecords, totalCount);
        }

        List<object> parameters = [modelId, organizationId];
        StringBuilder sql = new("SELECT id AS \"Value\" FROM data_records WHERE model_id = {0} AND organization_id = {1} AND deleted_at IS NULL");
        int paramIndex = 2;

        if (hasSearch)
        {
            sql.Append($" AND data::text ILIKE {{{paramIndex}}}");
            parameters.Add($"%{search}%");
            paramIndex++;
        }

        if (hasFilters && filters != null)
        {
            foreach (string filter in filters)
            {
                string[] parts = filter.Split('|');
                if (parts.Length < 2) continue;

                string fieldName = parts[0].Replace("\"", "");
                if (!SafeFieldRegex.IsMatch(fieldName)) continue;
                string op = parts[1].ToLowerInvariant();
                string val = parts.Length > 2 ? parts[2] : "";

                switch (op)
                {
                    case "eq":
                        sql.Append($" AND data->>'{fieldName}' = {{{paramIndex}}}");
                        parameters.Add(val);
                        paramIndex++;
                        break;
                    case "contains":
                        sql.Append($" AND data->>'{fieldName}' ILIKE {{{paramIndex}}}");
                        parameters.Add($"%{val}%");
                        paramIndex++;
                        break;
                    case "gt":
                        sql.Append($" AND (data->>'{fieldName}')::numeric > {{{paramIndex}}}::numeric");
                        parameters.Add(val);
                        paramIndex++;
                        break;
                    case "lt":
                        sql.Append($" AND (data->>'{fieldName}')::numeric < {{{paramIndex}}}::numeric");
                        parameters.Add(val);
                        paramIndex++;
                        break;
                    case "isempty":
                        sql.Append($" AND (data->>'{fieldName}' IS NULL OR data->>'{fieldName}' = '')");
                        break;
                    case "isnotempty":
                        sql.Append($" AND (data->>'{fieldName}' IS NOT NULL AND data->>'{fieldName}' != '')");
                        break;
                }
            }
        }

        string sortColumn = "created_at";
        bool isDescending = sortDesc ?? true;

        if (hasCustomSort && !string.IsNullOrWhiteSpace(sortBy))
        {
            string safeSortBy = sortBy.Replace("\"", "").Replace("'", "");
            if (safeSortBy == "created_at" || safeSortBy == "updated_at" || safeSortBy == "id")
            {
                sortColumn = safeSortBy;
            }
            else
            {
                if (SafeFieldRegex.IsMatch(safeSortBy))
                {
                    sortColumn = $"data->>'{safeSortBy}'";
                }
            }
        }

        string sortDir = isDescending ? "DESC" : "ASC";

        sql.Append($" ORDER BY {sortColumn} {sortDir}");

        List<Guid> orderedIds = await context.Database
            .SqlQueryRaw<Guid>(sql.ToString(), parameters.ToArray())
            .ToListAsync(ct);

        int total = orderedIds.Count;
        List<Guid> pageIdsList = orderedIds.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        if (pageIdsList.Count == 0)
            return (new List<DataRecord>(), total);

        List<DataRecord> records = await context.DataRecords
            .Where(r => pageIdsList.Contains(r.Id))
            .ToListAsync(ct);

        Dictionary<Guid, DataRecord> dict = records.ToDictionary(r => r.Id);
        List<DataRecord> sortedRecords = pageIdsList.Select(id => dict[id]).ToList();

        return (sortedRecords, total);
    }
}
