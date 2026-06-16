using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Axis.DataModeling.Application.Queries.GetRecords;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Infrastructure.Persistence;
using Axis.Shared.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Axis.DataModeling.Infrastructure.Repositories;

internal sealed class DataRecordRepository(
    DataModelingDbContext context,
    ITenantContext tenantContext) : IDataRecordRepository
{
    // Field names must match the same regex enforced by RecordFilter.TryParse and DataModel.
    private static readonly Regex SafeFieldName = new(@"^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);

    public async Task AddAsync(DataRecord record, CancellationToken ct = default)
        => await context.DataRecords.AddAsync(record, ct);

    public async Task<DataRecord?> GetByIdAsync(Guid id, Guid modelId, Guid tenantId, CancellationToken ct = default)
        => await context.DataRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.ModelId == modelId && r.tenantId == tenantId, ct);

    public async Task<IReadOnlyList<DataRecord>> GetAllAsync(Guid modelId, Guid tenantId, CancellationToken ct = default)
        => await context.DataRecords
            .Where(r => r.ModelId == modelId && r.tenantId == tenantId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<DataRecord> Records, int TotalCount)> GetPagedAsync(
        Guid modelId, Guid tenantId,
        int page, int pageSize,
        string? search = null,
        IReadOnlyList<RecordFilter>? filters = null,
        string? sortBy = null, string? sortDir = null,
        CancellationToken ct = default)
    {
        (string where, object[] parameters) = BuildWhere(modelId, tenantId, search, filters);
        string orderBy = BuildOrderBy(sortBy, sortDir);
        int offset = (page - 1) * pageSize;
        string table = SchemaTable();

        // Use string concatenation (not $"" interpolation) to avoid EF1002 on SqlQueryRaw.
        // The positional {n} placeholders in the where clause become parameterized SQL parameters.
        List<int> counts = await context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM " + table + " WHERE " + where, parameters)
            .ToListAsync(ct);

        int total = counts.FirstOrDefault();
        if (total == 0)
            return (Array.Empty<DataRecord>(), 0);

        // Fetch only the IDs in the correct page/order, then materialise full aggregates.
        string idSql = "SELECT id AS \"Value\" FROM " + table + " WHERE " + where
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
        IReadOnlyList<Guid> ids, Guid modelId, Guid tenantId,
        CancellationToken ct = default)
    {
        if (ids.Count == 0) return 0;

        Guid[] idArray = ids.ToArray();
        string table = SchemaTable();
        string sql = $$"""
            UPDATE {{table}}
               SET deleted_at = NOW()
             WHERE id = ANY({0})
               AND model_id = {1}
               AND tenant_id = {2}
               AND deleted_at IS NULL
            """;
        return await context.Database.ExecuteSqlRawAsync(sql, [idArray, modelId, tenantId], ct);
    }

    public async IAsyncEnumerable<DataRecord> GetAllForExportAsync(
        Guid modelId, Guid tenantId,
        string? search = null,
        IReadOnlyList<RecordFilter>? filters = null,
        string? sortBy = null, string? sortDir = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        (string where, object[] parameters) = BuildWhere(modelId, tenantId, search, filters);
        string orderBy = BuildOrderBy(sortBy, sortDir);
        string table = SchemaTable();

        // Load all matching IDs once (sorted), then stream records in chunks.
        string idSql = "SELECT id AS \"Value\" FROM " + table + " WHERE " + where + " ORDER BY " + orderBy;
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
    /// Returns the fully schema-qualified table name, e.g. <c>"tenant_acme"."data_records"</c>.
    /// Raw SQL must use this to respect the tenant schema rather than relying solely on search_path.
    /// </summary>
    private string SchemaTable()
        => $"\"{tenantContext.SchemaName}\".\"data_records\"";

    /// <summary>
    /// Builds a positional-parameter WHERE clause for data_records raw SQL queries.
    /// Validates all filter field names and operators defensively — throws if invalid input
    /// reaches here despite the upstream RecordFilter.TryParse guard.
    /// </summary>
    private static (string Sql, object[] Parameters) BuildWhere(
        Guid modelId, Guid tenantId,
        string? search, IReadOnlyList<RecordFilter>? filters)
    {
        List<object> parameters = [modelId, tenantId];
        StringBuilder sb = new("model_id = {0} AND tenant_id = {1} AND deleted_at IS NULL");
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
                // Fail closed: validate field name even if the caller skipped RecordFilter.TryParse.
                if (!SafeFieldName.IsMatch(f.Field))
                    throw new ArgumentException($"Invalid filter field name: '{f.Field}'.", nameof(filters));

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

                    default:
                        throw new ArgumentException($"Unsupported filter operator: '{f.Op}'.", nameof(filters));
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
            _ => "data->>'" + sortBy + "' " + dir
        };
    }
}
