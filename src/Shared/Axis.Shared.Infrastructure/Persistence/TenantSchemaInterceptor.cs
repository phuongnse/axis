using System.Data.Common;
using Axis.Shared.Application.Tenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Axis.Shared.Infrastructure.Persistence;

/// <summary>
/// Sets PostgreSQL search_path to the tenant's schema on every connection open.
/// This applies to both new and pooled connections, ensuring all EF Core queries
/// target the correct tenant schema without requiring per-tenant compiled models.
/// </summary>
public sealed class TenantSchemaInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetSearchPathAsync(connection, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = BuildSearchPathSql();
        cmd.ExecuteNonQuery();
    }

    private async Task SetSearchPathAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = BuildSearchPathSql();
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private string BuildSearchPathSql() =>
        $"SET search_path TO \"{tenantContext.SchemaName}\", public";
}
