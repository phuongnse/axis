using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class WorkspaceMembershipConfiguration : IEntityTypeConfiguration<WorkspaceMembership>
{
    public void Configure(EntityTypeBuilder<WorkspaceMembership> builder)
    {
        builder.ToTable("workspace_memberships"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); builder.Property(x => x.WorkspaceId).HasColumnName("workspace_id"); builder.Property(x => x.UserId).HasColumnName("user_id"); builder.Property(x => x.Role).HasColumnName("role").HasConversion<string>(); builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>(); builder.Property(x => x.IsProductBuilder).HasColumnName("is_product_builder").HasDefaultValue(false); builder.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired(); builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property<ActorKind>("CreatedByKind").HasColumnName("created_by_kind").HasConversion<string>().HasMaxLength(32).IsRequired(); builder.Property<Guid?>("CreatedBySubjectId").HasColumnName("created_by_subject_id"); builder.Property<string>("CreatedByDisplayName").HasColumnName("created_by_display_name").HasMaxLength(ActorSnapshot.MaximumDisplayNameLength).IsRequired(); builder.Property<ActorKind>("UpdatedByKind").HasColumnName("updated_by_kind").HasConversion<string>().HasMaxLength(32).IsRequired(); builder.Property<Guid?>("UpdatedBySubjectId").HasColumnName("updated_by_subject_id"); builder.Property<string>("UpdatedByDisplayName").HasColumnName("updated_by_display_name").HasMaxLength(ActorSnapshot.MaximumDisplayNameLength).IsRequired(); builder.Ignore(x => x.CreatedBy); builder.Ignore(x => x.UpdatedBy);
        builder.HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique(); builder.HasIndex(x => new { x.UserId, x.Status }); builder.HasIndex(x => new { x.WorkspaceId, x.Role }).IsUnique().HasFilter("role = 'Owner' AND status = 'Active'"); builder.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict); builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
