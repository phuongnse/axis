using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Infrastructure.Persistence.Configurations;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<WorkspaceModuleProvisioning> WorkspaceModuleProvisions => Set<WorkspaceModuleProvisioning>();
    public DbSet<User> Users => Set<User>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    internal DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    internal DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    internal DbSet<WorkspaceRegistrationToken> WorkspaceRegistrationTokens =>
        Set<WorkspaceRegistrationToken>();
    internal DbSet<RegistrationIdempotencyRecord> RegistrationIdempotencyRecords =>
        Set<RegistrationIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WorkspaceConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionPlanConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspacePlanChangeLogConfiguration());
        modelBuilder.ApplyConfiguration(new RegistrationIdempotencyRecordConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceModuleProvisioningConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceMembershipConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceMembershipRoleConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new InvitationConfiguration());
        modelBuilder.ApplyConfiguration(new PasswordResetTokenConfiguration());
        modelBuilder.ApplyConfiguration(new EmailVerificationTokenConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceRegistrationTokenConfiguration());

        // Register OpenIddict entity model (Applications, Authorizations, Scopes, Tokens)
        modelBuilder.UseOpenIddict();
    }
}
