using Axis.Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationMembershipConfiguration : IEntityTypeConfiguration<OrganizationMembership>
{ public void Configure(EntityTypeBuilder<OrganizationMembership> builder) { builder.ToTable("organization_memberships"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); builder.Property(x => x.OrganizationId).HasColumnName("organization_id"); builder.Property(x => x.UserId).HasColumnName("user_id"); builder.Property(x => x.Role).HasColumnName("role").HasConversion<string>(); builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>(); builder.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken(); builder.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique(); builder.HasIndex(x => new { x.UserId, x.Status }); builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict); builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict); } }
