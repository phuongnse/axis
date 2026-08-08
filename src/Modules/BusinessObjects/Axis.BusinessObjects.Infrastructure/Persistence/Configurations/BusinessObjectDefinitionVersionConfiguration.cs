using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.BusinessObjects.Infrastructure.Persistence.Configurations;

internal sealed class BusinessObjectDefinitionVersionConfiguration : IEntityTypeConfiguration<BusinessObjectDefinitionVersion>
{
    public void Configure(EntityTypeBuilder<BusinessObjectDefinitionVersion> builder)
    {
        builder.ToTable("business_object_definition_versions");
        builder.HasKey(version => version.Id);

        builder.Property(version => version.Id)
            .HasColumnName("id")
            .HasConversion(BusinessObjectValueConverters.DefinitionVersionId)
            .ValueGeneratedNever();

        builder.Property(version => version.SourceDefinitionId)
            .HasColumnName("source_definition_id")
            .HasConversion(BusinessObjectValueConverters.DefinitionId)
            .IsRequired();

        builder.Property(version => version.WorkspaceId)
            .HasColumnName("workspace_id")
            .IsRequired();

        builder.Property(version => version.VersionNumber)
            .HasColumnName("version_number")
            .IsRequired();

        builder.Property(version => version.Name)
            .HasColumnName("name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(version => version.Key)
            .HasColumnName("object_key")
            .HasMaxLength(63)
            .HasConversion(BusinessObjectValueConverters.DefinitionKey)
            .IsRequired();

        ComplexPropertyBuilder<SubjectReference> publishedBy = builder.ComplexProperty(version => version.PublishedBySubject);
        publishedBy.Property(subject => subject.Kind)
            .HasColumnName("published_by_subject_kind")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        publishedBy.Property(subject => subject.Id)
            .HasColumnName("published_by_subject_id")
            .IsRequired();

        builder.Property(version => version.PublishedAt)
            .HasColumnName("published_at")
            .IsRequired();

        builder.HasIndex(version => new { version.SourceDefinitionId, version.VersionNumber })
            .IsUnique();

        builder.HasIndex(version => new { version.WorkspaceId, version.Key, version.VersionNumber })
            .IsUnique();

        builder.HasMany(version => version.Fields)
            .WithOne()
            .HasForeignKey("BusinessObjectDefinitionVersionId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Navigation(version => version.Fields)
            .HasField("_fields")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
