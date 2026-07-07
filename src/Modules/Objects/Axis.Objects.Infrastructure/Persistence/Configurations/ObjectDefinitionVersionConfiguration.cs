using Axis.Objects.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Objects.Infrastructure.Persistence.Configurations;

internal sealed class ObjectDefinitionVersionConfiguration : IEntityTypeConfiguration<ObjectDefinitionVersion>
{
    public void Configure(EntityTypeBuilder<ObjectDefinitionVersion> builder)
    {
        builder.ToTable("object_definition_versions");
        builder.HasKey(version => version.Id);

        builder.Property(version => version.Id)
            .HasColumnName("id")
            .HasConversion(ObjectValueConverters.DefinitionVersionId)
            .ValueGeneratedNever();

        builder.Property(version => version.ObjectDefinitionId)
            .HasColumnName("object_definition_id")
            .HasConversion(ObjectValueConverters.DefinitionId)
            .IsRequired();

        builder.Property(version => version.WorkspaceId)
            .HasColumnName("workspace_id")
            .IsRequired();

        builder.Property(version => version.VersionNumber)
            .HasColumnName("version_number")
            .IsRequired();

        builder.Property(version => version.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(version => version.Key)
            .HasColumnName("object_key")
            .HasMaxLength(63)
            .HasConversion(ObjectValueConverters.DefinitionKey)
            .IsRequired();

        builder.Property(version => version.PublishedByUserId)
            .HasColumnName("published_by_user_id")
            .IsRequired();

        builder.Property(version => version.PublishedAt)
            .HasColumnName("published_at")
            .IsRequired();

        builder.HasIndex(version => new { version.ObjectDefinitionId, version.VersionNumber })
            .IsUnique();

        builder.HasIndex(version => new { version.WorkspaceId, version.Key, version.VersionNumber })
            .IsUnique();

        builder.HasMany(version => version.Fields)
            .WithOne()
            .HasForeignKey("ObjectDefinitionVersionId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Navigation(version => version.Fields)
            .HasField("_fields")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
