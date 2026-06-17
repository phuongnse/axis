using System.Data.Common;
using Axis.Shared.Application.Workspaces;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Axis.Shared.Infrastructure.Persistence;

/// <summary>
/// Sets PostgreSQL search_path to the workspace's schema on every connection open.
/// This applies to both new and pooled connections, ensuring all EF Core queries
/// target the correct workspace schema without requiring per-workspace compiled models.
/// </summary>
public sealed class WorkspaceSchemaInterceptor(IWorkspaceContext workspaceContext) : DbConnectionInterceptor
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
        using DbCommand cmd = connection.CreateCommand();
        cmd.CommandText = BuildSearchPathSql();
        cmd.ExecuteNonQuery();
    }

    private async Task SetSearchPathAsync(DbConnection connection, CancellationToken ct)
    {
        await using DbCommand cmd = connection.CreateCommand();
        cmd.CommandText = BuildSearchPathSql();
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private string BuildSearchPathSql() =>
        $"SET search_path TO \"{workspaceContext.SchemaName}\", public";
}
