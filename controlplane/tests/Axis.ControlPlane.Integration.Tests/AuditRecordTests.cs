using Axis.ControlPlane.Audit;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>The audit records against a real PostgreSQL: they are appended through the control plane's code and nothing changes or removes them.</summary>
public sealed class AuditRecordTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private NpgsqlDataSource _dataSource = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
        await ControlPlaneDatabase.MigrateAsync(_container.GetConnectionString(), TestContext.Current.CancellationToken);
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task An_appended_record_holds_its_actor_action_subject_and_time()
    {
        var token = TestContext.Current.CancellationToken;
        await AppendAsync(AuditActorKind.Job, "job-1", "subscription.extended", "subscription-1");

        await using var connection = await _dataSource.OpenConnectionAsync(token);
        var record = Assert.Single(await AuditRecords.ReadAsync(connection, "subscription-1", token));
        Assert.Equal("job", record.ActorKind);
        Assert.Equal("job-1", record.Actor);
        Assert.Equal("subscription.extended", record.Action);
        Assert.True(Math.Abs((DateTimeOffset.UtcNow - record.OccurredAt).TotalMinutes) < 5);
    }

    [Theory]
    [InlineData("Account", "account")]
    [InlineData("Webhook", "webhook")]
    [InlineData("Job", "job")]
    [InlineData("Operator", "operator")]
    public async Task Every_actor_kind_is_stored_in_lowercase(string kind, string stored)
    {
        var token = TestContext.Current.CancellationToken;
        var subject = $"kind-{stored}";
        await AppendAsync(Enum.Parse<AuditActorKind>(kind), "someone", "invoice.issued", subject);

        await using var connection = await _dataSource.OpenConnectionAsync(token);
        Assert.Equal(stored, Assert.Single(await AuditRecords.ReadAsync(connection, subject, token)).ActorKind);
    }

    [Fact]
    public async Task A_record_cannot_be_updated_deleted_or_truncated_over_the_control_plane_connection()
    {
        var token = TestContext.Current.CancellationToken;
        await AppendAsync(AuditActorKind.Operator, "operator-1", "trial.extended", "subscription-2");

        foreach (var sql in new[]
        {
            "UPDATE control_plane.audit_records SET action = 'changed'",
            "UPDATE control_plane.audit_records SET actor = 'someone-else' WHERE subject = 'subscription-2'",
            "DELETE FROM control_plane.audit_records",
            "DELETE FROM control_plane.audit_records WHERE subject = 'subscription-2'",
            "TRUNCATE control_plane.audit_records",
        })
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => ControlPlaneDatabase.ExecuteAsync(_dataSource, sql, token));
            Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);
        }

        await using var connection = await _dataSource.OpenConnectionAsync(token);
        var record = Assert.Single(await AuditRecords.ReadAsync(connection, "subscription-2", token));
        Assert.Equal("operator-1", record.Actor);
        Assert.Equal("trial.extended", record.Action);
    }

    [Fact]
    public async Task A_record_with_an_unknown_actor_kind_is_refused()
    {
        var token = TestContext.Current.CancellationToken;
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ControlPlaneDatabase.ExecuteAsync(
            _dataSource,
            "INSERT INTO control_plane.audit_records (id, actor_kind, actor, action, subject) VALUES (gen_random_uuid(), 'robot', 'a', 'b', 'c')",
            token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    private async Task AppendAsync(AuditActorKind kind, string actor, string action, string subject)
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await AuditRecords.AppendAsync(transaction, kind, actor, action, subject, token);
        await transaction.CommitAsync(token);
    }
}
