using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Infrastructure.Persistence.Configurations;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<TenantModuleProvisioning> TenantModuleProvisions => Set<TenantModuleProvisioning>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<ExternalRegistrationSession> ExternalRegistrationSessions => Set<ExternalRegistrationSession>();
    public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();
    internal DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    internal DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    internal DbSet<RegistrationIdempotencyRecord> RegistrationIdempotencyRecords =>
        Set<RegistrationIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionPlanConfiguration());
        modelBuilder.ApplyConfiguration(new OrganizationPlanChangeLogConfiguration());
        modelBuilder.ApplyConfiguration(new RegistrationIdempotencyRecordConfiguration());
        modelBuilder.ApplyConfiguration(new TenantModuleProvisioningConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new InvitationConfiguration());
        modelBuilder.ApplyConfiguration(new ExternalRegistrationSessionConfiguration());
        modelBuilder.ApplyConfiguration(new UserExternalLoginConfiguration());
        modelBuilder.ApplyConfiguration(new PasswordResetTokenConfiguration());
        modelBuilder.ApplyConfiguration(new EmailVerificationTokenConfiguration());

        // Register OpenIddict entity model (Applications, Authorizations, Scopes, Tokens)
        modelBuilder.UseOpenIddict();
    }
}
