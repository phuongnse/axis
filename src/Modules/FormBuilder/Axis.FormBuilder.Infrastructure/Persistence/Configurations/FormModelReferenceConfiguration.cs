using Axis.FormBuilder.Domain.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.FormBuilder.Infrastructure.Persistence.Configurations;

internal sealed class FormModelReferenceConfiguration : IEntityTypeConfiguration<FormModelReference>
{
    public void Configure(EntityTypeBuilder<FormModelReference> builder)
    {
        builder.ToTable("form_model_references");
        builder.HasKey(r => new { r.FormId, r.FormFieldId });
        builder.Property(r => r.FormId).HasColumnName("form_id");
        builder.Property(r => r.FormFieldId).HasColumnName("form_field_id");
        builder.Property(r => r.ModelId).HasColumnName("model_id").IsRequired();
        builder.Property(r => r.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(r => r.IsBroken).HasColumnName("is_broken").IsRequired();
        builder.HasIndex(r => new { r.ModelId, r.OrganizationId });
        builder.HasIndex(r => r.FormId);
    }
}
