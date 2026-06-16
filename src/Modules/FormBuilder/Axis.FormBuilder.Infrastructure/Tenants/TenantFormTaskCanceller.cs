using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Shared.Application.Tenants;
using Axis.Shared.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Axis.FormBuilder.Infrastructure.Tenants;

internal sealed class TenantFormTaskCanceller(IConfiguration configuration) : ITenantFormTaskCanceller
{
    public async Task CancelPendingForTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetConnectionString("FormBuilder")
            ?? throw new InvalidOperationException("ConnectionStrings:FormBuilder is required.");

        DbContextOptionsBuilder<FormBuilderDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);

        await using FormBuilderDbContext context = new(
            optionsBuilder.Options,
            new FixedTenantContext(tenantId));

        List<FormSubmission> pending = await context.FormSubmissions
            .Where(s => s.tenantId == tenantId && s.Status == FormSubmissionStatus.Pending)
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
