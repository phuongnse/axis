using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Objects.Infrastructure.Persistence.Configurations;

internal sealed class ObjectFieldVariantConfiguration : IEntityTypeConfiguration<ObjectFieldVariant>
{
    public void Configure(EntityTypeBuilder<ObjectFieldVariant> builder)
    {
        builder.ToTable("object_definition_field_variants");
        builder.HasKey(variant => variant.Id);

        builder.Property(variant => variant.Id)
            .HasColumnName("id")
            .HasConversion(ObjectValueConverters.FieldVariantId)
            .ValueGeneratedNever();

        builder.Property<ObjectFieldDefinitionId>("ObjectFieldDefinitionId")
            .HasColumnName("object_field_definition_id")
            .HasConversion(ObjectValueConverters.FieldDefinitionId)
            .IsRequired();

        ConfigureVariantColumns(builder);

        builder.HasIndex("ObjectFieldDefinitionId", nameof(ObjectFieldVariant.Kind))
            .IsUnique();

        builder.HasIndex("ObjectFieldDefinitionId", nameof(ObjectFieldVariant.Order));
    }

    internal static void ConfigureVariantColumns<TVariant>(EntityTypeBuilder<TVariant> builder)
        where TVariant : class
    {
        builder.Property<ObjectFieldVariantKind>(nameof(ObjectFieldVariant.Kind))
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property<int>(nameof(ObjectFieldVariant.Order))
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property<decimal?>(nameof(ObjectFieldVariant.MinNumber))
            .HasColumnName("min_number")
            .HasPrecision(18, 4);

        builder.Property<decimal?>(nameof(ObjectFieldVariant.MaxNumber))
            .HasColumnName("max_number")
            .HasPrecision(18, 4);

        builder.Property<DateOnly?>(nameof(ObjectFieldVariant.MinDate))
            .HasColumnName("min_date");

        builder.Property<DateOnly?>(nameof(ObjectFieldVariant.MaxDate))
            .HasColumnName("max_date");

        builder.Property<int?>(nameof(ObjectFieldVariant.MinLength))
            .HasColumnName("min_length");

        builder.Property<int?>(nameof(ObjectFieldVariant.MaxLength))
            .HasColumnName("max_length");

        builder.Property<string?>(nameof(ObjectFieldVariant.Pattern))
            .HasColumnName("pattern")
            .HasMaxLength(500);

        builder.Property<string[]>(nameof(ObjectFieldVariant.Options))
            .HasColumnName("options")
            .HasColumnType("text[]")
            .IsRequired();
    }
}
