using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Axis.DataModeling.Application.Queries.GetRecords;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.DataModeling.Infrastructure.Repositories;

internal sealed class DataRecordRepository(DataModelingDbContext context) : IDataRecordRepository
{
    // Field names must match the same regex enforced by RecordFilter.TryParse and DataModel.
    private static readonly Regex SafeFieldName = new(@"^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);

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
        string? search = null,
        IReadOnlyList<RecordFilter>? filters = null,
        string? sortBy = null, string? sortDir = null,
        CancellationToken ct = default)
    {
        (string where, object[] parameters) = BuildWhere(modelId, organizationId, search, filters);
        string orderBy = BuildOrderBy(sortBy, sortDir);
        int offset = (page - 1) * pageSize;

        // Use string concatenation (not $"" interpolation) to avoid EF1002 on SqlQueryRaw.
        // The positional {n} placeholders in the where clause become parameterized SQL parameters.
        List<int> counts = await context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM data_records WHERE " + where, parameters)
            .ToListAsync(ct);

        int total = counts.FirstOrDefault();
        if (total == 0)
            return (Array.Empty<DataRecord>(), 0);

        // Fetch only the IDs in the correct page/order, then materialise full aggregates.
        string idSql = "SELECT id AS \"Value\" FROM data_records WHERE " + where
            + " ORDER BY " + orderBy
            + " LIMIT " + pageSize + " OFFSET " + offset;

        List<Guid> pageIds = await context.Database
            .SqlQueryRaw<Guid>(idSql, parameters)
            .ToListAsync(ct);

        HashSet<Guid> idSet = pageIds.ToHashSet();
        Dictionary<Guid, DataRecord> recordMap = (await context.DataRecords
            .Where(r => idSet.Contains(r.Id))
            .ToListAsync(ct))
            .ToDictionary(r => r.Id);

        // Re-order to match the SQL ORDER BY result.
        List<DataRecord> ordered = pageIds
            .Where(id => recordMap.ContainsKey(id))
            .Select(id => recordMap[id])
            .ToList();

        return (ordered, total);
    }

    public async Task<int> BulkDeleteAsync(
        IReadOnlyList<Guid> ids, Guid modelId, Guid organizationId,
        CancellationToken ct = default)
    {
        if (ids.Count == 0) return 0;

        Guid[] idArray = ids.ToArray();
        return await context.Database.ExecuteSqlAsync(
            $"""
            UPDATE data_records
               SET deleted_at = NOW()
             WHERE id = ANY({idArray})
               AND model_id = {modelId}
               AND organization_id = {organizationId}
               AND deleted_at IS NULL
            """, ct);
    }

    public async IAsyncEnumerable<DataRecord> GetAllForExportAsync(
        Guid modelId, Guid organizationId,
        string? search = null,
        IReadOnlyList<RecordFilter>? filters = null,
        string? sortBy = null, string? sortDir = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        (string where, object[] parameters) = BuildWhere(modelId, organizationId, search, filters);
        string orderBy = BuildOrderBy(sortBy, sortDir);

        // Load all matching IDs once (sorted), then stream records in chunks.
        string idSql = "SELECT id AS \"Value\" FROM data_records WHERE " + where + " ORDER BY " + orderBy;
        List<Guid> allIds = await context.Database
            .SqlQueryRaw<Guid>(idSql, parameters)
            .ToListAsync(ct);

        const int chunkSize = 500;
        for (int i = 0; i < allIds.Count; i += chunkSize)
        {
            List<Guid> chunk = allIds.GetRange(i, Math.Min(chunkSize, allIds.Count - i));
            HashSet<Guid> chunkSet = chunk.ToHashSet();

            Dictionary<Guid, DataRecord> chunkMap = (await context.DataRecords
                .AsNoTracking()
                .Where(r => chunkSet.Contains(r.Id))
                .ToListAsync(ct))
                .ToDictionary(r => r.Id);

            foreach (Guid id in chunk)
            {
                if (chunkMap.TryGetValue(id, out DataRecord? record))
                    yield return record;
            }
        }
    }

    // ── Query helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a positional-parameter WHERE clause for data_records raw SQL queries.
    /// Field names in filters are pre-validated by RecordFilter.TryParse before reaching here.
    /// </summary>
    private static (string Sql, object[] Parameters) BuildWhere(
        Guid modelId, Guid organizationId,
        string? search, IReadOnlyList<RecordFilter>? filters)
    {
        List<object> parameters = [modelId, organizationId];
        StringBuilder sb = new("model_id = {0} AND organization_id = {1} AND deleted_at IS NULL");
        int idx = 2;

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add($"%{search}%");
            sb.Append(" AND data::text ILIKE {" + idx++ + "}");
        }

        if (filters is { Count: > 0 })
        {
            foreach (RecordFilter f in filters)
            {
                // Field name validated by RecordFilter.TryParse regex — safe to embed in SQL.
                string col = "data->>'" + f.Field + "'";

                switch (f.Op)
                {
                    case "eq":
                        parameters.Add(f.Value);
                        sb.Append(" AND " + col + " = {" + idx++ + "}");
                        break;

                    case "contains":
                        parameters.Add("%" + f.Value + "%");
                        sb.Append(" AND " + col + " ILIKE {" + idx++ + "}");
                        break;

                    case "gt":
                        parameters.Add(f.Value);
                        sb.Append(" AND " + col + " > {" + idx++ + "}");
                        break;

                    case "lt":
                        parameters.Add(f.Value);
                        sb.Append(" AND " + col + " < {" + idx++ + "}");
                        break;

                    case "isempty":
                        sb.Append(" AND (" + col + " IS NULL OR " + col + " = '')");
                        break;

                    case "isnotempty":
                        sb.Append(" AND (" + col + " IS NOT NULL AND " + col + " != '')");
                        break;
                }
            }
        }

        return (sb.ToString(), parameters.ToArray());
    }

    /// <summary>
    /// Builds a safe ORDER BY clause. Only fields matching the allowed regex are honoured;
    /// anything else falls back to <c>created_at DESC</c>.
    /// </summary>
    private static string BuildOrderBy(string? sortBy, string? sortDir)
    {
        string dir = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

        if (string.IsNullOrWhiteSpace(sortBy) || !SafeFieldName.IsMatch(sortBy))
            return "created_at DESC";

        // System columns map to actual DB columns; custom fields use JSONB text extraction.
        return sortBy.ToLowerInvariant() switch
        {
            "created_at" => "created_at " + dir,
            "updated_at" => "updated_at " + dir,
            _            => "data->>'" + sortBy + "' " + dir
        };
    }
}
