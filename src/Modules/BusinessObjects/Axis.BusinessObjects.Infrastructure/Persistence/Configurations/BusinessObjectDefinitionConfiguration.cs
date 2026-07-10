using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.BusinessObjects.Infrastructure.Persistence.Configurations;

internal sealed class BusinessObjectDefinitionConfiguration : IEntityTypeConfiguration<BusinessObjectDefinition>
{
    public void Configure(EntityTypeBuilder<BusinessObjectDefinition> builder)
    {
        builder.ToTable("business_object_definitions");
        builder.HasKey(definition => definition.Id);

        builder.Property(definition => definition.Id)
            .HasColumnName("id")
            .HasConversion(BusinessObjectValueConverters.DefinitionId)
            .ValueGeneratedNever();

        builder.Property<uint>("xmin")
            .IsRowVersion();

        builder.Property(definition => definition.WorkspaceId)
            .HasColumnName("workspace_id")
            .IsRequired();

        builder.Property(definition => definition.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(definition => definition.Key)
            .HasColumnName("object_key")
            .HasMaxLength(63)
            .HasConversion(BusinessObjectValueConverters.DefinitionKey)
            .IsRequired();

        builder.Property(definition => definition.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(definition => definition.Revision)
            .HasColumnName("revision")
            .IsRequired();

        builder.Property(definition => definition.LatestPublishedVersionNumber)
            .HasColumnName("latest_published_version_number");

        builder.Property(definition => definition.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(definition => definition.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(definition => new { definition.WorkspaceId, definition.Key })
            .IsUnique();

        builder.HasMany(definition => definition.Fields)
            .WithOne()
            .HasForeignKey("BusinessObjectDefinitionId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasMany(definition => definition.Versions)
            .WithOne()
            .HasForeignKey(version => version.SourceDefinitionId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Navigation(definition => definition.Fields)
            .HasField("_fields")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(definition => definition.Versions)
            .HasField("_versions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
