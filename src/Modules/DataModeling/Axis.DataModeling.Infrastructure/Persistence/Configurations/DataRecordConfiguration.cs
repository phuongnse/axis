using System.Text.Json;
using Axis.DataModeling.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.DataModeling.Infrastructure.Persistence.Configurations;

internal sealed class DataRecordConfiguration : IEntityTypeConfiguration<DataRecord>
{
    private static readonly JsonSerializerOptions DataJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly ValueConverter<Dictionary<string, object?>, string> DataConverter = new(
        dict => JsonSerializer.Serialize(dict, DataJsonOptions),
        json => JsonSerializer.Deserialize<Dictionary<string, object?>>(json, DataJsonOptions) ?? new());

    private static readonly ValueComparer<Dictionary<string, object?>> DataComparer = new(
        (a, b) => JsonSerializer.Serialize(a, DataJsonOptions) == JsonSerializer.Serialize(b, DataJsonOptions),
        v => JsonSerializer.Serialize(v, DataJsonOptions).GetHashCode(),
        v => JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonSerializer.Serialize(v, DataJsonOptions), DataJsonOptions) ?? new());

    public void Configure(EntityTypeBuilder<DataRecord> builder)
    {
        builder.ToTable("data_records");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.ModelId)
            .HasColumnName("model_id")
            .IsRequired();

        builder.Property(r => r.OrganizationId)
            .HasColumnName("organization_id")
            .IsRequired();

        builder.Property(r => r.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(r => r.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasQueryFilter(r => !r.DeletedAt.HasValue);

        builder.Property<Dictionary<string, object?>>("_data")
            .HasField("_data")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("data")
            .HasColumnType("jsonb")
            .HasConversion(DataConverter, DataComparer)
            .IsRequired();

        builder.Ignore(r => r.Data);

        builder.HasIndex(r => new { r.ModelId, r.OrganizationId });
    }
}
