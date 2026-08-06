using Axis.Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{ public void Configure(EntityTypeBuilder<Organization> builder) { builder.ToTable("organizations"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(Organization.MaxNameLength).IsRequired(); builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired(); builder.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken(); } }
