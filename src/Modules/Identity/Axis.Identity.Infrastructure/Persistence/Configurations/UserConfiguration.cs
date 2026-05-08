using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");

        builder.Property(u => u.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(u => u.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(u => u.AvatarUrl)
            .HasColumnName("avatar_url")
            .HasMaxLength(2048);

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired()
            .HasConversion(new ValueConverter<Email, string>(
                e => e.Value,
                s => Email.Create(s).Value!));

        builder.HasIndex(u => new { u.Email, u.OrganizationId }).IsUnique();

        builder.Property(u => u.OrganizationId)
            .HasColumnName("organization_id")
            .IsRequired();

        builder.Property(u => u.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(u => u.IsEmailVerified)
            .HasColumnName("is_email_verified")
            .IsRequired();

        builder.Property(u => u.FailedLoginAttempts)
            .HasColumnName("failed_login_attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(u => u.LockedUntil)
            .HasColumnName("locked_until");

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        // Shadow property for password hash — not part of domain
        builder.Property<string?>("PasswordHash")
            .HasColumnName("password_hash");

        // Map private backing field _roleIds as a uuid[] column
        builder.PrimitiveCollection<List<Guid>>("_roleIds")
            .HasField("_roleIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("role_ids")
            .IsRequired();

        // Computed / domain-only properties — not stored
        builder.Ignore(u => u.FullName);
        builder.Ignore(u => u.IsLockedOut);
        builder.Ignore(u => u.RoleIds);
    }
}
