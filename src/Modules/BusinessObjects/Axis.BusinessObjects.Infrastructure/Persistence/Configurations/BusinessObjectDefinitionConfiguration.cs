using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace Axis.BusinessObjects.Infrastructure.Persistence.Configurations;

internal sealed class BusinessObjectDefinitionConfiguration : IEntityTypeConfiguration<BusinessObjectDefinition>
{
    public void Configure(EntityTypeBuilder<BusinessObjectDefinition> builder)
    {
        builder.ToTable("business_object_definitions", table => table.HasCheckConstraint(
            "CK_business_object_definitions_installed_provenance",
            """
            (installed_solution_version_id IS NULL AND installed_component_key IS NULL AND
             installed_component_hash IS NULL AND installed_operation_id IS NULL AND
             installed_step_id IS NULL AND installed_lease_epoch IS NULL)
            OR
            (installed_solution_version_id IS NOT NULL AND installed_component_key IS NOT NULL AND
             installed_component_hash IS NOT NULL AND installed_operation_id IS NOT NULL AND
             installed_step_id IS NOT NULL AND installed_lease_epoch > 0 AND
             installed_component_key ~ '^[a-z][a-z0-9_.:@-]{0,199}$' AND
             installed_component_hash ~ '^[0-9a-f]{64}$' AND
             installed_solution_version_id <> '00000000-0000-0000-0000-000000000000'::uuid AND
             installed_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND
             installed_step_id <> '00000000-0000-0000-0000-000000000000'::uuid)
            """));
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
            .HasMaxLength(256)
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

        builder.Property<ActorKind>("CreatedByKind")
            .HasColumnName("created_by_kind")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property<Guid?>("CreatedBySubjectId")
            .HasColumnName("created_by_subject_id");
        builder.Property<string>("CreatedByDisplayName")
            .HasColumnName("created_by_display_name")
            .HasMaxLength(ActorSnapshot.MaximumDisplayNameLength)
            .IsRequired();
        builder.Property<ActorKind>("UpdatedByKind")
            .HasColumnName("updated_by_kind")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property<Guid?>("UpdatedBySubjectId")
            .HasColumnName("updated_by_subject_id");
        builder.Property<string>("UpdatedByDisplayName")
            .HasColumnName("updated_by_display_name")
            .HasMaxLength(ActorSnapshot.MaximumDisplayNameLength)
            .IsRequired();
        builder.Ignore(definition => definition.CreatedBy);
        builder.Ignore(definition => definition.UpdatedBy);

        builder.Property(definition => definition.InstalledSolutionVersionId)
            .HasColumnName("installed_solution_version_id");
        builder.Property(definition => definition.InstalledComponentKey)
            .HasColumnName("installed_component_key")
            .HasMaxLength(200);
        builder.Property(definition => definition.InstalledComponentHash)
            .HasColumnName("installed_component_hash")
            .HasMaxLength(64);
        builder.Property(definition => definition.InstalledOperationId)
            .HasColumnName("installed_operation_id");
        builder.Property(definition => definition.InstalledStepId)
            .HasColumnName("installed_step_id");
        builder.Property(definition => definition.InstalledLeaseEpoch)
            .HasColumnName("installed_lease_epoch");
        builder.Ignore(definition => definition.IsInstalled);

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

        builder.HasIndex(definition => new { definition.WorkspaceId, definition.InstalledComponentKey })
            .IsUnique()
            .HasFilter("installed_component_key IS NOT NULL");

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
