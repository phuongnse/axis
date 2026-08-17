using Microsoft.EntityFrameworkCore;

namespace Axis.Authorization.Infrastructure.Persistence;

public sealed class AuthorizationDbContext(DbContextOptions<AuthorizationDbContext> options) : DbContext(options)
{
    public DbSet<ProductRoleAssignmentRow> Assignments => Set<ProductRoleAssignmentRow>();
    public DbSet<AuthorizationIdempotencyRow> IdempotencyRecords => Set<AuthorizationIdempotencyRow>();
    public DbSet<AuthorizationAuditOutboxRow> AuditOutbox => Set<AuthorizationAuditOutboxRow>();
    public DbSet<InstalledPolicyRow> Policies => Set<InstalledPolicyRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductRoleAssignmentRow>(row =>
        {
            row.ToTable("authorization_product_role_assignments"); row.HasKey(value => value.Id); row.Property(value => value.Id).HasColumnName("id"); row.Property(value => value.WorkspaceId).HasColumnName("workspace_id"); row.Property(value => value.SubjectKind).HasColumnName("subject_kind"); row.Property(value => value.SubjectId).HasColumnName("subject_id"); row.Property(value => value.PolicyVersionId).HasColumnName("policy_version_id"); row.Property(value => value.RoleKey).HasColumnName("role_key").HasMaxLength(200);
            row.Property(value => value.IsActive).HasColumnName("is_active"); row.Property(value => value.Revision).HasColumnName("revision"); row.Property<uint>("xmin").IsRowVersion();
            row.Property(value => value.CreatedAt).HasColumnName("created_at"); row.Property(value => value.RevokedAt).HasColumnName("revoked_at");
            row.Property(value => value.UpdatedAt).HasColumnName("updated_at");
            row.Property(value => value.CreatedByKind).HasColumnName("created_by_kind").HasMaxLength(32);
            row.Property(value => value.CreatedBySubjectId).HasColumnName("created_by_subject_id");
            row.Property(value => value.CreatedByDisplayName).HasColumnName("created_by_display_name").HasMaxLength(200);
            row.Property(value => value.UpdatedByKind).HasColumnName("updated_by_kind").HasMaxLength(32);
            row.Property(value => value.UpdatedBySubjectId).HasColumnName("updated_by_subject_id");
            row.Property(value => value.UpdatedByDisplayName).HasColumnName("updated_by_display_name").HasMaxLength(200);
            row.HasIndex(value => new { value.WorkspaceId, value.SubjectKind, value.SubjectId, value.PolicyVersionId, value.RoleKey }).IsUnique();
        });
        modelBuilder.Entity<AuthorizationIdempotencyRow>(row =>
        {
            row.ToTable("authorization_idempotency");
            row.HasKey(value => new { value.WorkspaceId, value.IdempotencyKey });
            row.Property(value => value.WorkspaceId).HasColumnName("workspace_id");
            row.Property(value => value.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(120);
            row.Property(value => value.RequestDigest).HasColumnName("request_digest").HasMaxLength(64);
            row.Property(value => value.Operation).HasColumnName("operation").HasMaxLength(16);
            row.Property(value => value.AssignmentId).HasColumnName("assignment_id");
            row.Property(value => value.AuditEventId).HasColumnName("audit_event_id");
            row.Property(value => value.CreatedAt).HasColumnName("created_at");
            row.HasIndex(value => value.AssignmentId);
        });
        modelBuilder.Entity<AuthorizationAuditOutboxRow>(row =>
        {
            row.ToTable("authorization_audit_outbox");
            row.HasKey(value => value.Id);
            row.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
            row.Property(value => value.OccurredAt).HasColumnName("occurred_at");
            row.Property(value => value.Payload).HasColumnName("payload").HasColumnType("jsonb");
            row.Property(value => value.DeliveryState).HasColumnName("delivery_state").HasMaxLength(24);
            row.Property(value => value.ReadBackAt).HasColumnName("read_back_at");
            row.Property(value => value.AttemptCount).HasColumnName("attempt_count");
            row.Property(value => value.LastAttemptAt).HasColumnName("last_attempt_at");
            row.Property(value => value.NextAttemptAt).HasColumnName("next_attempt_at");
            row.Property(value => value.LeaseId).HasColumnName("lease_id");
            row.Property(value => value.LeaseUntil).HasColumnName("lease_until");
            row.Property(value => value.FailureReason).HasColumnName("failure_reason").HasMaxLength(256);
            row.Property(value => value.CreatedAt).HasColumnName("created_at");
            row.Property(value => value.Revision).HasColumnName("revision").IsConcurrencyToken();
            row.HasIndex(value => new { value.DeliveryState, value.NextAttemptAt });
        });
        modelBuilder.Entity<InstalledPolicyRow>(row => { row.ToTable("authorization_installed_policies"); row.HasKey(value => new { value.WorkspaceId, value.VersionId }); row.Property(value => value.WorkspaceId).HasColumnName("workspace_id"); row.Property(value => value.VersionId).HasColumnName("version_id"); row.Property(value => value.PolicyKey).HasColumnName("policy_key").HasMaxLength(200); row.Property(value => value.CanonicalContent).HasColumnName("canonical_content").HasColumnType("text"); row.Property(value => value.Provenance).HasColumnName("provenance"); row.Property(value => value.InstalledAt).HasColumnName("installed_at"); });
    }
}

public sealed class ProductRoleAssignmentRow
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string SubjectKind { get; set; } = null!;
    public Guid SubjectId { get; set; }
    public Guid PolicyVersionId { get; set; }
    public string RoleKey { get; set; } = null!;
    public bool IsActive { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? CreatedByKind { get; set; }
    public Guid? CreatedBySubjectId { get; set; }
    public string? CreatedByDisplayName { get; set; }
    public string? UpdatedByKind { get; set; }
    public Guid? UpdatedBySubjectId { get; set; }
    public string? UpdatedByDisplayName { get; set; }
}
public sealed class AuthorizationIdempotencyRow { public Guid WorkspaceId { get; set; } public string IdempotencyKey { get; set; } = null!; public string RequestDigest { get; set; } = null!; public string Operation { get; set; } = null!; public Guid AssignmentId { get; set; } public Guid AuditEventId { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class AuthorizationAuditOutboxRow
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Payload { get; set; } = null!;
    public string DeliveryState { get; set; } = "Pending";
    public DateTimeOffset? ReadBackAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Revision { get; set; }
}
public sealed class InstalledPolicyRow { public Guid VersionId { get; set; } public Guid WorkspaceId { get; set; } public string PolicyKey { get; set; } = null!; public string CanonicalContent { get; set; } = null!; public string Provenance { get; set; } = null!; public DateTimeOffset InstalledAt { get; set; } }
