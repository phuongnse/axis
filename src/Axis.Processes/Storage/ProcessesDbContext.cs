using Axis.Processes.Instances;
using Microsoft.EntityFrameworkCore;

namespace Axis.Processes.Storage;

/// <summary>
/// The processes module's tables in a tenant database: the process instances, the history of
/// their steps, their human tasks, their staged connector calls, the attempts and lookups of those calls and of package calls, the events they raised, the receipts of starts and of
/// endpoint calls that carried an <c>Idempotency-Key</c>, the receipts of trigger starts, the counted failures of deliveries and their dead letters, the state of schedule triggers, and the work that workers claim and run. The caller supplies the connection to the tenant database.
/// </summary>
public sealed class ProcessesDbContext(DbContextOptions<ProcessesDbContext> options) : DbContext(options)
{
    public const string Schema = "axis";

    /// <summary>
    /// Each module context in the <c>axis</c> schema keeps its own migrations history table, so
    /// their histories do not collide.
    /// </summary>
    public const string MigrationsHistoryTable = "__processes_migrations";

    public DbSet<ProcessInstanceRow> Instances => Set<ProcessInstanceRow>();

    public DbSet<ProcessStepHistoryRow> StepHistory => Set<ProcessStepHistoryRow>();

    public DbSet<ProcessTaskRow> Tasks => Set<ProcessTaskRow>();

    public DbSet<ConnectorCallRow> ConnectorCalls => Set<ConnectorCallRow>();

    public DbSet<PackageCallRow> PackageCalls => Set<PackageCallRow>();

    public DbSet<ProcessCallAttemptRow> CallAttempts => Set<ProcessCallAttemptRow>();

    public DbSet<EventRow> Events => Set<EventRow>();

    public DbSet<ProcessStartReceiptRow> StartReceipts => Set<ProcessStartReceiptRow>();

    public DbSet<EndpointReceiptRow> EndpointReceipts => Set<EndpointReceiptRow>();

    public DbSet<TriggerStartRow> TriggerStarts => Set<TriggerStartRow>();

    public DbSet<DeliveryFailureRow> DeliveryFailures => Set<DeliveryFailureRow>();

    public DbSet<DeadLetterRow> DeadLetters => Set<DeadLetterRow>();

    public DbSet<TriggerScheduleRow> TriggerSchedules => Set<TriggerScheduleRow>();

    public DbSet<WorkItemRow> WorkItems => Set<WorkItemRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ProcessInstanceRow>(instance =>
        {
            instance.ToTable("process_instances");
            instance.HasKey(i => i.Id).HasName("pk_process_instances");
            instance.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
            instance.Property(i => i.ApplicationId).HasColumnName("application_id");
            instance.Property(i => i.ProcessId).HasColumnName("process_id");
            instance.Property(i => i.SubjectEntityId).HasColumnName("subject_entity_id");
            instance.Property(i => i.SubjectId).HasColumnName("subject_id");
            instance.Property(i => i.ReleaseId).HasColumnName("release_id");
            instance.Property(i => i.State).HasColumnName("state");
            instance.Property(i => i.Revision).HasColumnName("revision");
            instance.Property(i => i.Step).HasColumnName("step");
            instance.Property(i => i.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            instance.Property(i => i.EndedAt).HasColumnName("ended_at");

            // One start per submission: a record has at most one running or waiting instance of a process.
            instance.HasIndex(i => new { i.ApplicationId, i.ProcessId, i.SubjectId })
                .IsUnique()
                .HasFilter("state IN ('running', 'waiting')")
                .HasDatabaseName(ProcessStarts.ActiveSubjectIndex);

            // The inspector lists a process's instances newest first, by state and by subject record.
            instance.HasIndex(i => new { i.ApplicationId, i.ProcessId, i.CreatedAt, i.Id })
                .IsDescending(false, false, true, true)
                .HasDatabaseName("ix_process_instances_application_id_process_id_created_at_id");
            instance.HasIndex(i => new { i.ApplicationId, i.ProcessId, i.State, i.CreatedAt, i.Id })
                .IsDescending(false, false, false, true, true)
                .HasDatabaseName("ix_process_instances_application_id_process_id_state_created_at");
            instance.HasIndex(i => i.SubjectId).HasDatabaseName("ix_process_instances_subject_id");
        });

        modelBuilder.Entity<ProcessStepHistoryRow>(history =>
        {
            history.ToTable("process_step_history");
            history.HasKey(h => h.Id).HasName("pk_process_step_history");
            history.Property(h => h.Id).HasColumnName("id").ValueGeneratedNever();
            history.Property(h => h.ProcessInstanceId).HasColumnName("process_instance_id");
            history.Property(h => h.Step).HasColumnName("step");
            history.Property(h => h.Revision).HasColumnName("revision");
            history.Property(h => h.Input).HasColumnName("input").HasColumnType("jsonb");
            history.Property(h => h.Output).HasColumnName("output").HasColumnType("jsonb");
            history.Property(h => h.Decision).HasColumnName("decision");
            history.Property(h => h.Error).HasColumnName("error");
            history.Property(h => h.StartedAt).HasColumnName("started_at");
            history.Property(h => h.FinishedAt).HasColumnName("finished_at");
            history.Property(h => h.TraceId).HasColumnName("trace_id");
            history.HasIndex(h => h.ProcessInstanceId).HasDatabaseName("ix_process_step_history_process_instance_id");
            history.HasOne<ProcessInstanceRow>()
                .WithMany()
                .HasForeignKey(h => h.ProcessInstanceId)
                .HasConstraintName("fk_process_step_history_process_instances_process_instance_id")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProcessTaskRow>(task =>
        {
            task.ToTable("process_tasks");
            task.HasKey(t => t.Id).HasName("pk_process_tasks");
            task.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
            task.Property(t => t.ProcessInstanceId).HasColumnName("process_instance_id");
            task.Property(t => t.ProcessId).HasColumnName("process_id");
            task.Property(t => t.ReleaseId).HasColumnName("release_id");
            task.Property(t => t.ApplicationId).HasColumnName("application_id");
            task.Property(t => t.Step).HasColumnName("step");
            task.Property(t => t.SubjectEntityId).HasColumnName("subject_entity_id");
            task.Property(t => t.SubjectId).HasColumnName("subject_id");
            task.Property(t => t.AssigneeKind).HasColumnName("assignee_kind");
            task.Property(t => t.Assignee).HasColumnName("assignee");
            task.Property(t => t.FormId).HasColumnName("form_id");
            task.Property(t => t.DueAt).HasColumnName("due_at");
            task.Property(t => t.State).HasColumnName("state");
            task.Property(t => t.Outcome).HasColumnName("outcome");
            task.Property(t => t.CompletedBy).HasColumnName("completed_by");
            task.Property(t => t.CompletedAt).HasColumnName("completed_at");
            task.Property(t => t.ExpiredAt).HasColumnName("expired_at");
            task.Property(t => t.ClaimedBy).HasColumnName("claimed_by");
            task.Property(t => t.ClaimedAt).HasColumnName("claimed_at");
            task.Property(t => t.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            task.HasIndex(t => t.ProcessInstanceId).HasDatabaseName("ix_process_tasks_process_instance_id");

            // The task inbox finds a user's open tasks by their id or by any role they hold.
            task.HasIndex(t => new { t.ApplicationId, t.State, t.AssigneeKind, t.Assignee })
                .HasDatabaseName("ix_process_tasks_application_id_state_assignee");
            task.HasOne<ProcessInstanceRow>()
                .WithMany()
                .HasForeignKey(t => t.ProcessInstanceId)
                .HasConstraintName("fk_process_tasks_process_instances_process_instance_id")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConnectorCallRow>(call =>
        {
            call.ToTable("connector_calls");
            call.HasKey(c => c.Id).HasName("pk_connector_calls");
            call.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            call.Property(c => c.ProcessInstanceId).HasColumnName("process_instance_id");
            call.Property(c => c.Step).HasColumnName("step");
            call.Property(c => c.StagedRevision).HasColumnName("staged_revision");
            call.Property(c => c.ReleaseId).HasColumnName("release_id");
            call.Property(c => c.Connector).HasColumnName("connector");
            call.Property(c => c.Operation).HasColumnName("operation");
            call.Property(c => c.State).HasColumnName("state");
            call.Property(c => c.Attempts).HasColumnName("attempts");
            call.Property(c => c.Lookups).HasColumnName("lookups").HasDefaultValue(0);
            call.Property(c => c.Input).HasColumnName("input").HasColumnType("jsonb");
            call.Property(c => c.LastStatus).HasColumnName("last_status");
            call.Property(c => c.LastError).HasColumnName("last_error");
            call.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            call.Property(c => c.SettledAt).HasColumnName("settled_at");

            // A step stages at most one call per pass.
            call.HasIndex(c => new { c.ProcessInstanceId, c.Step, c.StagedRevision })
                .IsUnique()
                .HasDatabaseName("ux_connector_calls_process_instance_id_step_staged_revision");
            call.HasOne<ProcessInstanceRow>()
                .WithMany()
                .HasForeignKey(c => c.ProcessInstanceId)
                .HasConstraintName("fk_connector_calls_process_instances_process_instance_id")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PackageCallRow>(call =>
        {
            call.ToTable("package_calls");
            call.HasKey(c => c.Id).HasName("pk_package_calls");
            call.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            call.Property(c => c.ProcessInstanceId).HasColumnName("process_instance_id");
            call.Property(c => c.Step).HasColumnName("step");
            call.Property(c => c.StagedRevision).HasColumnName("staged_revision");
            call.Property(c => c.ReleaseId).HasColumnName("release_id");
            call.Property(c => c.Package).HasColumnName("package");
            call.Property(c => c.Version).HasColumnName("version");
            call.Property(c => c.Operation).HasColumnName("operation");
            call.Property(c => c.State).HasColumnName("state");
            call.Property(c => c.Attempts).HasColumnName("attempts");

            // The defaults are the retry policy and flag of a call staged before they were stored.
            call.Property(c => c.RetryAttempts).HasColumnName("retry_attempts").HasDefaultValue(3);
            call.Property(c => c.RetryBackoff).HasColumnName("retry_backoff").HasColumnType("interval").HasDefaultValueSql("'30 seconds'::interval");
            call.Property(c => c.Idempotent).HasColumnName("idempotent").HasDefaultValue(false);
            call.Property(c => c.Input).HasColumnName("input").HasColumnType("jsonb");
            call.Property(c => c.LastError).HasColumnName("last_error");
            call.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            call.Property(c => c.SettledAt).HasColumnName("settled_at");

            // A step stages at most one call per pass.
            call.HasIndex(c => new { c.ProcessInstanceId, c.Step, c.StagedRevision })
                .IsUnique()
                .HasDatabaseName("ux_package_calls_process_instance_id_step_staged_revision");
            call.HasOne<ProcessInstanceRow>()
                .WithMany()
                .HasForeignKey(c => c.ProcessInstanceId)
                .HasConstraintName("fk_package_calls_process_instances_process_instance_id")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProcessCallAttemptRow>(attempt =>
        {
            attempt.ToTable("process_call_attempts");
            attempt.HasKey(a => a.Id).HasName("pk_process_call_attempts");
            attempt.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            attempt.Property(a => a.CallKind).HasColumnName("call_kind");
            attempt.Property(a => a.CallId).HasColumnName("call_id");
            attempt.Property(a => a.Kind).HasColumnName("kind");
            attempt.Property(a => a.Number).HasColumnName("number");
            attempt.Property(a => a.StartedAt).HasColumnName("started_at");
            attempt.Property(a => a.FinishedAt).HasColumnName("finished_at");
            attempt.Property(a => a.Outcome).HasColumnName("outcome");
            attempt.Property(a => a.HttpStatus).HasColumnName("http_status");
            attempt.Property(a => a.Error).HasColumnName("error");
            attempt.Property(a => a.TraceId).HasColumnName("trace_id");

            // The call is in one of two tables, so there is no foreign key. Each attempt and lookup has its own number.
            attempt.HasIndex(a => new { a.CallId, a.Kind, a.Number })
                .IsUnique()
                .HasDatabaseName("ux_process_call_attempts_call_id_kind_number");
        });

        modelBuilder.Entity<EventRow>(@event =>
        {
            @event.ToTable("events");
            @event.HasKey(e => e.Id).HasName("pk_events");
            @event.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            @event.Property(e => e.ApplicationId).HasColumnName("application_id");
            @event.Property(e => e.Event).HasColumnName("event");
            @event.Property(e => e.ReleaseId).HasColumnName("release_id");
            @event.Property(e => e.EntityId).HasColumnName("entity_id");
            @event.Property(e => e.SubjectId).HasColumnName("subject_id");
            @event.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb");
            @event.Property(e => e.ProcessInstanceId).HasColumnName("process_instance_id");
            @event.Property(e => e.RaisedAt).HasColumnName("raised_at").HasDefaultValueSql("now()");
            @event.HasIndex(e => e.ProcessInstanceId).HasDatabaseName("ix_events_process_instance_id");
            @event.HasOne<ProcessInstanceRow>()
                .WithMany()
                .HasForeignKey(e => e.ProcessInstanceId)
                .HasConstraintName("fk_events_process_instances_process_instance_id")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProcessStartReceiptRow>(receipt =>
        {
            receipt.ToTable("process_start_receipts");
            receipt.HasKey(r => new { r.ApplicationId, r.ProcessId, r.Key }).HasName("pk_process_start_receipts");
            receipt.Property(r => r.ApplicationId).HasColumnName("application_id");
            receipt.Property(r => r.ProcessId).HasColumnName("process_id");
            receipt.Property(r => r.Key).HasColumnName("key");
            receipt.Property(r => r.SubjectId).HasColumnName("subject_id");
            receipt.Property(r => r.InstanceId).HasColumnName("instance_id");
            receipt.Property(r => r.StatusCode).HasColumnName("status_code");
            receipt.Property(r => r.Body).HasColumnName("body").HasColumnType("jsonb");
            receipt.Property(r => r.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<EndpointReceiptRow>(receipt =>
        {
            receipt.ToTable("endpoint_receipts");
            receipt.HasKey(r => new { r.ApplicationId, r.EndpointId, r.Caller, r.Key }).HasName("pk_endpoint_receipts");
            receipt.Property(r => r.ApplicationId).HasColumnName("application_id");
            receipt.Property(r => r.EndpointId).HasColumnName("endpoint_id");
            receipt.Property(r => r.Caller).HasColumnName("caller");
            receipt.Property(r => r.Key).HasColumnName("key");
            receipt.Property(r => r.RecordId).HasColumnName("record_id");
            receipt.Property(r => r.InstanceId).HasColumnName("instance_id");
            receipt.Property(r => r.InputHash).HasColumnName("input_hash").HasColumnType("bytea");
            receipt.Property(r => r.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<TriggerStartRow>(start =>
        {
            start.ToTable("trigger_starts");

            // One start per trigger, cause and record.
            start.HasKey(s => new { s.TriggerId, s.Source, s.SubjectId }).HasName("pk_trigger_starts");
            start.Property(s => s.TriggerId).HasColumnName("trigger_id");
            start.Property(s => s.Source).HasColumnName("source");
            start.Property(s => s.SubjectId).HasColumnName("subject_id");
            start.Property(s => s.Outcome).HasColumnName("outcome");
            start.Property(s => s.ProcessInstanceId).HasColumnName("process_instance_id");
            start.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<DeliveryFailureRow>(failure =>
        {
            failure.ToTable("delivery_failures");
            failure.HasKey(f => new { f.Kind, f.Target }).HasName("pk_delivery_failures");
            failure.Property(f => f.Kind).HasColumnName("kind");
            failure.Property(f => f.Target).HasColumnName("target");
            failure.Property(f => f.Attempts).HasColumnName("attempts");
            failure.Property(f => f.LastError).HasColumnName("last_error");
            failure.Property(f => f.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<DeadLetterRow>(letter =>
        {
            letter.ToTable("dead_letters");
            letter.HasKey(l => l.Id).HasName("pk_dead_letters");
            letter.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
            letter.Property(l => l.Kind).HasColumnName("kind");
            letter.Property(l => l.ApplicationId).HasColumnName("application_id");
            letter.Property(l => l.ProcessId).HasColumnName("process_id");
            letter.Property(l => l.EventId).HasColumnName("event_id");
            letter.Property(l => l.TriggerId).HasColumnName("trigger_id");
            letter.Property(l => l.ProcessInstanceId).HasColumnName("process_instance_id");
            letter.Property(l => l.EmailId).HasColumnName("email_id");
            letter.Property(l => l.OccurrenceAt).HasColumnName("occurrence_at");
            letter.Property(l => l.Attempts).HasColumnName("attempts");
            letter.Property(l => l.LastError).HasColumnName("last_error");
            letter.Property(l => l.State).HasColumnName("state");
            letter.Property(l => l.Reason).HasColumnName("reason");
            letter.Property(l => l.SettledBy).HasColumnName("settled_by");
            letter.Property(l => l.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            letter.Property(l => l.SettledAt).HasColumnName("settled_at");

            // The inspector lists a process's dead letters by state.
            letter.HasIndex(l => new { l.ApplicationId, l.ProcessId, l.State })
                .HasDatabaseName("ix_dead_letters_application_id_process_id_state");
        });

        modelBuilder.Entity<TriggerScheduleRow>(schedule =>
        {
            schedule.ToTable("trigger_schedules");
            schedule.HasKey(s => s.TriggerId).HasName("pk_trigger_schedules");
            schedule.Property(s => s.TriggerId).HasColumnName("trigger_id").ValueGeneratedNever();
            schedule.Property(s => s.ApplicationId).HasColumnName("application_id");
            schedule.Property(s => s.ReleaseId).HasColumnName("release_id");
            schedule.Property(s => s.Schedule).HasColumnName("schedule");
            schedule.Property(s => s.TimeZone).HasColumnName("time_zone");
            schedule.Property(s => s.NextOccurrenceAt).HasColumnName("next_occurrence_at");
            schedule.Property(s => s.LastOccurrenceAt).HasColumnName("last_occurrence_at");
            schedule.HasIndex(s => s.ApplicationId).HasDatabaseName("ix_trigger_schedules_application_id");
        });

        modelBuilder.Entity<WorkItemRow>(item =>
        {
            item.ToTable("process_work_items");
            item.HasKey(i => i.Id).HasName("pk_process_work_items");
            item.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
            item.Property(i => i.TenantId).HasColumnName("tenant_id");
            item.Property(i => i.Kind).HasColumnName("kind");
            item.Property(i => i.Seq).HasColumnName("seq").UseIdentityAlwaysColumn();
            item.Property(i => i.DueAt).HasColumnName("due_at");
            item.Property(i => i.LeaseExpiresAt).HasColumnName("lease_expires_at");
            item.Property(i => i.ClaimToken).HasColumnName("claim_token");
            item.Property(i => i.ProcessInstanceId).HasColumnName("process_instance_id");
            item.Property(i => i.ConnectorCallId).HasColumnName("connector_call_id");
            item.Property(i => i.TaskId).HasColumnName("task_id");
            item.Property(i => i.PackageCallId).HasColumnName("package_call_id");
            item.Property(i => i.EventId).HasColumnName("event_id");
            item.Property(i => i.TriggerId).HasColumnName("trigger_id");
            item.Property(i => i.ReleaseId).HasColumnName("release_id");
            item.Property(i => i.OccurrenceAt).HasColumnName("occurrence_at");
            item.Property(i => i.Cursor).HasColumnName("cursor");
            item.Property(i => i.EntityId).HasColumnName("entity_id");
            item.Property(i => i.TraceId).HasColumnName("trace_id");
            item.Property(i => i.SpanId).HasColumnName("span_id");
            item.HasIndex(i => i.DueAt).HasDatabaseName("ix_process_work_items_due_at");
            item.HasIndex(i => i.ProcessInstanceId).HasDatabaseName("ix_process_work_items_process_instance_id");
            item.HasIndex(i => i.ConnectorCallId).HasDatabaseName("ix_process_work_items_connector_call_id");
            item.HasIndex(i => i.PackageCallId).HasDatabaseName("ix_process_work_items_package_call_id");
            item.HasIndex(i => i.TaskId).HasDatabaseName("ix_process_work_items_task_id");
            item.HasIndex(i => i.EventId).HasDatabaseName("ix_process_work_items_event_id");

            // A schedule trigger has at most one item that waits for its next occurrence.
            item.HasIndex(i => i.TriggerId)
                .IsUnique()
                .HasFilter("kind = 'trigger.schedule'")
                .HasDatabaseName("ux_process_work_items_schedule_trigger_id");
            item.HasOne<ProcessInstanceRow>()
                .WithMany()
                .HasForeignKey(i => i.ProcessInstanceId)
                .HasConstraintName("fk_process_work_items_process_instances_process_instance_id")
                .OnDelete(DeleteBehavior.Restrict);
            item.HasOne<ConnectorCallRow>()
                .WithMany()
                .HasForeignKey(i => i.ConnectorCallId)
                .HasConstraintName("fk_process_work_items_connector_calls_connector_call_id")
                .OnDelete(DeleteBehavior.Restrict);
            item.HasOne<ProcessTaskRow>()
                .WithMany()
                .HasForeignKey(i => i.TaskId)
                .HasConstraintName("fk_process_work_items_process_tasks_task_id")
                .OnDelete(DeleteBehavior.Restrict);
            item.HasOne<PackageCallRow>()
                .WithMany()
                .HasForeignKey(i => i.PackageCallId)
                .HasConstraintName("fk_process_work_items_package_calls_package_call_id")
                .OnDelete(DeleteBehavior.Restrict);
            item.HasOne<EventRow>()
                .WithMany()
                .HasForeignKey(i => i.EventId)
                .HasConstraintName("fk_process_work_items_events_event_id")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
