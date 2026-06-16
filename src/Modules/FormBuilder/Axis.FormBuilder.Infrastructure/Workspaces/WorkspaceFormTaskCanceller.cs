using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Shared.Application.Workspaces;
using Axis.Shared.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Axis.FormBuilder.Infrastructure.Workspaces;

internal sealed class WorkspaceFormTaskCanceller(IConfiguration configuration) : IWorkspaceFormTaskCanceller
{
    public async Task CancelPendingForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetConnectionString("FormBuilder")
            ?? throw new InvalidOperationException("ConnectionStrings:FormBuilder is required.");

        DbContextOptionsBuilder<FormBuilderDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);

        await using FormBuilderDbContext context = new(
            optionsBuilder.Options,
            new FixedWorkspaceContext(workspaceId));

        List<FormSubmission> pending = await context.FormSubmissions
            .Where(s => s.workspaceId == workspaceId && s.Status == FormSubmissionStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (FormSubmission submission in pending)
        {
            try
            {
                submission.Cancel();
            }
            catch (InvalidOperationException)
            {
                // Skip tasks that are no longer pending.
            }
        }

        if (pending.Count > 0)
            await context.SaveChangesAsync(cancellationToken);
    }
}
