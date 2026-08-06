using System.Data.Common;
using Axis.Audit.Contracts;
using Axis.Audit.Infrastructure.Extensions;
using Axis.Audit.Infrastructure.Persistence;
using Axis.Audit.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Audit.Infrastructure.Tests;

[Collection("AuditDb")]
public sealed class AuditPersistenceIntegrationTests(AuditDatabaseFixture db)
{
    [Fact]
    public async Task MigratedSink_WhenEventIsRetried_PersistsOneReadableRecord()
    {
        await using AuditDbContext context = db.CreateContext();
        (await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken))
            .Should().ContainSingle(migration => migration.EndsWith("_InitialAudit", StringComparison.Ordinal));

        AuditEventV1 auditEvent = Event();
        await using ServiceProvider provider = CreateProvider();
        using IServiceScope scope = provider.CreateScope();
        IAuditEventSink sink = scope.ServiceProvider.GetRequiredService<IAuditEventSink>();

        AuditIngestionResult first = await sink.IngestAsync(auditEvent, TestContext.Current.CancellationToken);
        AuditEventReadBackV1? initialReadBack = await sink.ReadBackAsync(auditEvent.EventId, TestContext.Current.CancellationToken);
        AuditIngestionResult retry = await sink.IngestAsync(auditEvent, TestContext.Current.CancellationToken);
        AuditEventReadBackV1? readBack = await sink.ReadBackAsync(auditEvent.EventId, TestContext.Current.CancellationToken);

        first.Disposition.Should().Be(AuditIngestionDisposition.Stored);
        initialReadBack.Should().BeEquivalentTo(first.Event);
        retry.Disposition.Should().Be(AuditIngestionDisposition.AlreadyStored);
        readBack.Should().BeEquivalentTo(first.Event);
        (await context.AuditRecords.CountAsync(record => record.EventId == auditEvent.EventId, TestContext.Current.CancellationToken))
            .Should().Be(1);
    }

    [Fact]
    public async Task MigratedSink_WhenEventIdPayloadConflicts_ReturnsConflictWithoutChangingTheRecord()
    {
        AuditEventV1 auditEvent = Event();
        await using ServiceProvider provider = CreateProvider();
        using IServiceScope scope = provider.CreateScope();
        IAuditEventSink sink = scope.ServiceProvider.GetRequiredService<IAuditEventSink>();
        await sink.IngestAsync(auditEvent, TestContext.Current.CancellationToken);

        AuditIngestionResult result = await sink.IngestAsync(
            auditEvent with { Outcome = "denied" }, TestContext.Current.CancellationToken);
        AuditEventReadBackV1? readBack = await sink.ReadBackAsync(auditEvent.EventId, TestContext.Current.CancellationToken);

        result.Disposition.Should().Be(AuditIngestionDisposition.Conflict);
        readBack!.Outcome.Should().Be("succeeded");
    }

    [Fact]
    public async Task AppendOnlyTrigger_WhenEfUpdateAndSqlDeleteAreAttempted_RejectsBothAndRetainsTheRecord()
    {
        AuditEventV1 auditEvent = Event();
        await using ServiceProvider provider = CreateProvider();
        using IServiceScope scope = provider.CreateScope();
        IAuditEventSink sink = scope.ServiceProvider.GetRequiredService<IAuditEventSink>();
        await sink.IngestAsync(auditEvent, TestContext.Current.CancellationToken);

        await using AuditDbContext context = db.CreateContext();
        Func<Task> efUpdate = () => context.AuditRecords
            .Where(record => record.EventId == auditEvent.EventId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(record => record.Outcome, "denied"), TestContext.Current.CancellationToken);
        InvalidOperationException efException = (await efUpdate.Should().ThrowAsync<InvalidOperationException>()).Which;
        efException.InnerException.Should().BeOfType<PostgresException>()
            .Which.MessageText.Should().Be("Audit records are append-only.");

        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "DELETE FROM audit_records WHERE event_id = @event_id";
        DbParameter eventId = command.CreateParameter();
        eventId.ParameterName = "event_id";
        eventId.Value = auditEvent.EventId;
        command.Parameters.Add(eventId);
        Func<Task> sqlDelete = () => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        await sqlDelete.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Audit records are append-only.");

        (await sink.ReadBackAsync(auditEvent.EventId, TestContext.Current.CancellationToken))
            .Should().NotBeNull();
    }

    private ServiceProvider CreateProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Audit"] = db.ConnectionString,
            })
            .Build();
        ServiceCollection services = new();
        services.AddAuditInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static AuditEventV1 Event() => new(
        Guid.NewGuid(), AuditActorKindV1.Human, Guid.NewGuid(), null, Guid.NewGuid(), "workspace.created", "workspace",
        Guid.NewGuid(), "succeeded", DateTimeOffset.UtcNow, $"correlation-{Guid.NewGuid():N}",
        new Dictionary<string, string> { ["transition_state"] = "completed" });
}
