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

        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(u => u.IsEmailVerified)
            .HasColumnName("is_email_verified")
            .IsRequired();

        builder.Property(u => u.AcceptedTermsVersion)
            .HasColumnName("accepted_terms_version")
            .HasMaxLength(32);

        builder.Property(u => u.AcceptedPrivacyVersion)
            .HasColumnName("accepted_privacy_version")
            .HasMaxLength(32);

        builder.Property(u => u.LegalAcceptedAt)
            .HasColumnName("legal_accepted_at");

        builder.Property(u => u.FailedLoginAttempts)
            .HasColumnName("failed_login_attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(u => u.LockedUntil)
            .HasColumnName("locked_until");

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash");

        builder.Ignore(u => u.FullName);
        builder.Ignore(u => u.IsLockedOut);
    }
}
