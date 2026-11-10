using System.Net;
using Axis.ControlPlane.SignUp;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The terms acceptances against a real PostgreSQL, see <c>docs/reference/sign-up.md#terms-acceptance</c>. The control plane's role
/// owns the tables, as in production, and keeps only <c>SELECT</c> and <c>INSERT</c> on them. A superuser skips the grants and meets
/// the trigger.
/// </summary>
public sealed class TermsAcceptanceTests : IAsyncLifetime
{
    private static readonly string[] _changes =
    [
        "UPDATE control_plane.terms_acceptances SET version = 'x'",
        "UPDATE control_plane.terms_acceptances SET account = 'y' WHERE tenant_id IS NOT NULL",
        "DELETE FROM control_plane.terms_acceptances",
        "DELETE FROM control_plane.terms_acceptances WHERE tenant_id IS NOT NULL",
        "TRUNCATE control_plane.terms_acceptances",
    ];

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private NpgsqlDataSource _role = null!;
    private NpgsqlDataSource _superuser = null!;

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await _container.StartAsync(token);
        await using (var admin = NpgsqlDataSource.Create(_container.GetConnectionString()))
        {
            await ControlPlaneDatabase.ExecuteAsync(admin, "CREATE ROLE axis_controlplane LOGIN PASSWORD 'terms-test'", token);
            await ControlPlaneDatabase.ExecuteAsync(admin, "CREATE DATABASE axis_controlplane OWNER axis_controlplane", token);
        }

        var superuser = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = "axis_controlplane" }.ConnectionString;
        var role = new NpgsqlConnectionStringBuilder(superuser) { Username = "axis_controlplane", Password = "terms-test" }.ConnectionString;
        await ControlPlaneDatabase.MigrateAsync(role, token);
        _role = NpgsqlDataSource.Create(role);
        _superuser = NpgsqlDataSource.Create(superuser);
    }

    public async ValueTask DisposeAsync()
    {
        await _role.DisposeAsync();
        await _superuser.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task The_role_that_owns_the_table_cannot_update_delete_or_truncate_an_acceptance()
    {
        var token = TestContext.Current.CancellationToken;
        var tenant = NewTenant();
        await AppendAsync(tenant, "role-account");

        foreach (var sql in _changes)
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => ControlPlaneDatabase.ExecuteAsync(_role, sql, token));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        }

        await AssertUnchangedAsync(tenant, "role-account");
    }

    [Fact]
    public async Task A_superuser_cannot_update_delete_or_truncate_an_acceptance_either()
    {
        var token = TestContext.Current.CancellationToken;
        var tenant = NewTenant();
        await AppendAsync(tenant, "superuser-account");

        foreach (var sql in _changes)
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => ControlPlaneDatabase.ExecuteAsync(_superuser, sql, token));
            Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);
        }

        await AssertUnchangedAsync(tenant, "superuser-account");
    }

    [Fact]
    public async Task An_acceptance_without_a_client_address_or_a_user_agent_is_stored_and_a_long_user_agent_is_cut()
    {
        var tenant = NewTenant();
        await AppendAsync(tenant, "account", clientAddress: null, userAgent: null);
        var longTenant = NewTenant();
        await AppendAsync(longTenant, "account", userAgent: new string('a', TermsAcceptances.MaxUserAgentLength + 100));

        Assert.Equal(1, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.terms_acceptances WHERE tenant_id = '{tenant}' AND client_address IS NULL AND user_agent IS NULL"));
        Assert.Equal(TermsAcceptances.MaxUserAgentLength, await ScalarAsync<int>($"SELECT length(user_agent) FROM control_plane.terms_acceptances WHERE tenant_id = '{longTenant}'"));
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("Terms")]
    [InlineData("")]
    public async Task An_acceptance_of_a_document_other_than_the_terms_or_the_dpa_is_refused(string document)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => AppendAsync(NewTenant(), "account", document));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    private static string NewTenant() => $"t{Guid.NewGuid():N}";

    private async Task AppendAsync(
        string tenant,
        string account,
        string document = TermsAcceptances.Terms,
        IPAddress? clientAddress = null,
        string? userAgent = "AxisTest/1.0")
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await _role.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await TermsAcceptances.AppendAsync(transaction, account, tenant, document, "2026-01", "2026-03", clientAddress, userAgent, token);
        await transaction.CommitAsync(token);
    }

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_role, sql, TestContext.Current.CancellationToken);

    private async Task AssertUnchangedAsync(string tenant, string account)
    {
        Assert.Equal(1, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.terms_acceptances WHERE tenant_id = '{tenant}'"));
        Assert.Equal("2026-01", await ScalarAsync<string>($"SELECT version FROM control_plane.terms_acceptances WHERE tenant_id = '{tenant}'"));
        Assert.Equal(account, await ScalarAsync<string>($"SELECT account FROM control_plane.terms_acceptances WHERE tenant_id = '{tenant}'"));
    }
}
