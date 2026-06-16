using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Infrastructure.Persistence.Configurations;
using Axis.Shared.Application.Workspaces;
using Axis.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.FormBuilder.Infrastructure.Persistence;

internal sealed class FormBuilderDbContext(
    DbContextOptions<FormBuilderDbContext> options,
    IWorkspaceContext workspaceContext) : DbContext(options)
{
    public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();
    public DbSet<FormWorkflowReference> FormWorkflowReferences => Set<FormWorkflowReference>();
    public DbSet<FormModelReference> FormModelReferences => Set<FormModelReference>();
    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Per ADR-017: interceptor wiring inlined per module.
        optionsBuilder.AddInterceptors(new WorkspaceSchemaInterceptor(workspaceContext));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new FormDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new FormWorkflowReferenceConfiguration());
        modelBuilder.ApplyConfiguration(new FormModelReferenceConfiguration());
        modelBuilder.ApplyConfiguration(new FormSubmissionConfiguration());
    }
}
