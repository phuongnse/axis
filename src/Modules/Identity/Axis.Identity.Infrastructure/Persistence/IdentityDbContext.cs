using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence.Configurations;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<User> Users => Set<User>();
    internal DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    internal DbSet<RegistrationIdempotencyRecord> RegistrationIdempotencyRecords =>
        Set<RegistrationIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WorkspaceConfiguration());
        modelBuilder.ApplyConfiguration(new RegistrationIdempotencyRecordConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new EmailVerificationTokenConfiguration());

        // Register OpenIddict entity model (Applications, Authorizations, Scopes, Tokens)
        modelBuilder.UseOpenIddict();
    }
}
