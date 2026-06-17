using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired()
            .HasConversion(new ValueConverter<Email, string>(
                e => e.Value,
                s => Email.Create(s).Value!));

        builder.Property(i => i.workspaceId)
            .HasColumnName("workspace_id")
            .IsRequired();

        builder.Property(i => i.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.Property(i => i.InvitedByUserId)
            .HasColumnName("invited_by_user_id")
            .IsRequired();

        builder.Property(i => i.Token)
            .HasColumnName("token")
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(i => i.Token).IsUnique();

        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(i => i.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(i => i.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Ignore(i => i.IsExpired);
    }
}
