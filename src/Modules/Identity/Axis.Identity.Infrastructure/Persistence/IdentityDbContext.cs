using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence.Configurations;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<WorkspaceContextTransition> WorkspaceContextTransitions => Set<WorkspaceContextTransition>();
    public DbSet<WorkspaceInvitation> WorkspaceInvitations => Set<WorkspaceInvitation>();
    public DbSet<ServiceIdentity> ServiceIdentities => Set<ServiceIdentity>();
    internal DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    internal DbSet<RegistrationIdempotencyRecord> RegistrationIdempotencyRecords =>
        Set<RegistrationIdempotencyRecord>();
    internal DbSet<CreateOrganizationIdempotencyRecordEntity> CreateOrganizationIdempotencyRecords =>
        Set<CreateOrganizationIdempotencyRecordEntity>();
    internal DbSet<IdentityAuditOutboxRecord> IdentityAuditOutboxRecords => Set<IdentityAuditOutboxRecord>();
    internal DbSet<ServiceAssertionReplayRecord> ServiceAssertionReplayRecords => Set<ServiceAssertionReplayRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WorkspaceConfiguration());
        modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
        modelBuilder.ApplyConfiguration(new OrganizationMembershipConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceMembershipConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceContextTransitionConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceInvitationConfiguration());
        modelBuilder.ApplyConfiguration(new ServiceIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new ServiceAssertionReplayRecordConfiguration());
        modelBuilder.ApplyConfiguration(new CreateOrganizationIdempotencyRecordConfiguration());
        modelBuilder.ApplyConfiguration(new IdentityAuditOutboxRecordConfiguration());
        modelBuilder.ApplyConfiguration(new RegistrationIdempotencyRecordConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new EmailVerificationTokenConfiguration());

        // Register OpenIddict entity model (Applications, Authorizations, Scopes, Tokens)
        modelBuilder.UseOpenIddict();
    }
}
