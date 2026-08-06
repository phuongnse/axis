using Axis.Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class WorkspaceInvitationConfiguration : IEntityTypeConfiguration<WorkspaceInvitation>
{
    public void Configure(EntityTypeBuilder<WorkspaceInvitation> builder)
    {
        builder.ToTable("workspace_invitations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id");
        builder.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
        builder.Property(x => x.InviterUserId).HasColumnName("inviter_user_id");
        builder.Property(x => x.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(320);
        builder.Property(x => x.RequestedRole).HasColumnName("requested_role").HasConversion<string>();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        builder.Property(x => x.TerminalMaterialPurgedAt).HasColumnName("terminal_material_purged_at");
        builder.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();

        builder.HasIndex(x => new { x.WorkspaceId, x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.WorkspaceId, x.NormalizedEmail, x.RequestedRole })
            .IsUnique()
            .HasFilter("status = 'Pending' AND normalized_email IS NOT NULL");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.InviterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        ConfigureTokenGenerations(builder);
        ConfigureHandoffs(builder);
    }

    private static void ConfigureTokenGenerations(EntityTypeBuilder<WorkspaceInvitation> invitation)
    {
        invitation.OwnsMany(x => x.TokenGenerations, builder =>
        {
            builder.ToTable("workspace_invitation_tokens");
            builder.WithOwner().HasForeignKey("invitation_id");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property<Guid>("invitation_id").HasColumnName("invitation_id");
            builder.Property(x => x.Generation).HasColumnName("generation");
            builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
            builder.Property(x => x.DeliveryEnvelope).HasColumnName("delivery_envelope");
            builder.Property(x => x.DeliveryCorrelation).HasColumnName("delivery_correlation").HasMaxLength(128);
            builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
            builder.Property(x => x.DeliveryStatus).HasColumnName("delivery_status").HasConversion<string>();
            builder.Property(x => x.DeliveryAttempts).HasColumnName("delivery_attempts");
            builder.Property(x => x.NextDeliveryAttemptAt).HasColumnName("next_delivery_attempt_at");
            builder.Property(x => x.LastDeliveryErrorCode).HasColumnName("last_delivery_error_code").HasMaxLength(128);
            builder.HasIndex("invitation_id", nameof(InvitationTokenGeneration.Generation)).IsUnique();
            builder.HasIndex(x => x.TokenHash).IsUnique();
            builder.HasIndex(x => new { x.DeliveryStatus, x.NextDeliveryAttemptAt });
        });

        invitation.Navigation(x => x.TokenGenerations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureHandoffs(EntityTypeBuilder<WorkspaceInvitation> invitation)
    {
        invitation.OwnsMany(x => x.Handoffs, builder =>
        {
            builder.ToTable("workspace_invitation_handoffs");
            builder.WithOwner().HasForeignKey("invitation_id");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property<Guid>("invitation_id").HasColumnName("invitation_id");
            builder.Property(x => x.TokenGeneration).HasColumnName("token_generation");
            builder.Property(x => x.HandoffHash).HasColumnName("handoff_hash").HasMaxLength(64);
            builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
            builder.HasIndex(x => x.HandoffHash).IsUnique();
            builder.HasIndex(x => new { x.Status, x.ExpiresAt });
        });

        invitation.Navigation(x => x.Handoffs).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
