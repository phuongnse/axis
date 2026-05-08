using System.Text.Json;
using Axis.DataModeling.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
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

        builder.Property(r => r.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property<Dictionary<string, object?>>("_data")
            .HasField("_data")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("data")
            .HasColumnType("jsonb")
            .HasConversion(DataConverter)
            .IsRequired();

        builder.Ignore(r => r.Data);

        builder.HasIndex(r => new { r.ModelId, r.OrganizationId });
    }
}
