using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Shared.Application.TeamAccounts;
using Axis.Shared.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Axis.FormBuilder.Infrastructure.TeamAccounts;

internal sealed class TeamAccountFormTaskCanceller(IConfiguration configuration) : ITeamAccountFormTaskCanceller
{
    public async Task CancelPendingForTeamAccountAsync(Guid teamAccountId, CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetConnectionString("FormBuilder")
            ?? throw new InvalidOperationException("ConnectionStrings:FormBuilder is required.");

        DbContextOptionsBuilder<FormBuilderDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);

        await using FormBuilderDbContext context = new(
            optionsBuilder.Options,
            new FixedTenantContext(teamAccountId));

        List<FormSubmission> pending = await context.FormSubmissions
            .Where(s => s.TeamAccountId == teamAccountId && s.Status == FormSubmissionStatus.Pending)
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
