using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

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

        builder.Property<string>("SearchTitle")
            .HasColumnName("search_title")
            .HasComputedColumnSql(
                "axis_unaccent(lower(coalesce(name, '')))",
                stored: true);

        builder.Property<string>("SearchText")
            .HasColumnName("search_text")
            .HasComputedColumnSql(
                "axis_unaccent(lower(coalesce(name, '') || ' ' || coalesce(object_key, '')))",
                stored: true);

        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasComputedColumnSql(
                "to_tsvector('simple', axis_unaccent(lower(coalesce(name, '') || ' ' || coalesce(object_key, ''))))",
                stored: true);

        builder.HasIndex(definition => new { definition.WorkspaceId, definition.Key })
            .IsUnique();

        builder.HasIndex("SearchTitle")
            .HasDatabaseName("ix_business_object_definitions_search_title")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        builder.HasIndex("SearchText")
            .HasDatabaseName("ix_business_object_definitions_search_text")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        builder.HasIndex("SearchVector")
            .HasDatabaseName("ix_business_object_definitions_search_vector")
            .HasMethod("gin");

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
