using Axis.Server.Tests;
using static Axis.Integration.Tests.Polling;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The server and the worker run on the runtime role, which has row access only. This class uses no
/// fixture helper that opens a connection as the schema role, so a session of any other role on the
/// tenant database would come from the server or the worker.
/// </summary>
public sealed class DatabaseRoleTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_server_and_worker_session_on_the_tenant_database_belongs_to_the_runtime_role()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var subject = await CreateRequestAsync(anna, 500);
        await StartAsync(anna, subject);

        Assert.True(await fixture.RunUntilIdleAsync(CancellationToken) > 0);

        // The check runs as the superuser, which is the only role that sees other roles' sessions.
        // Its own session is left out, and so is the session that ran the sample's activation, which has closed.
        await WaitUntilAsync(async () => PostgreSqlServer.RuntimeRole == (string?)await PostgreSqlServer.AdminScalarAsync(
            fixture.ConnectionString,
            """
            SELECT string_agg(DISTINCT usename, ',') FROM pg_stat_activity
            WHERE datname = current_database() AND pid <> pg_backend_pid() AND backend_type = 'client backend'
            """,
            CancellationToken), "The client sessions did not all use the runtime role.");
    }
}
