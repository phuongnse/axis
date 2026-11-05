using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// Opens the platform database over the schema role, <c>ConnectionStrings:PlatformSchema</c>, for the writes of the provisioning
/// API that need no job. The reader role of the server and the worker has no write right on the tables these writes change.
/// Each context gets a connection of its own that is never pooled, so the schema role keeps no idle session on the platform
/// database between two writes.
/// </summary>
internal sealed record CellPlatformWriter(string SchemaConnectionString)
{
    public PlatformDbContext Open() =>
        new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(SchemaConnectionString) { Pooling = false }.ConnectionString)
            .Options);
}
