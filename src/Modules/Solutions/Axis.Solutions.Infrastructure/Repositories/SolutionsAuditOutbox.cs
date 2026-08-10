using Axis.Solutions.Application;
using Axis.Solutions.Infrastructure.Persistence;
using Axis.Solutions.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Solutions.Infrastructure.Repositories;

internal sealed class SolutionsAuditOutbox(SolutionsDbContext context, TimeProvider clock) : ISolutionsAuditOutbox
{
    public async Task EnqueueAsync(SolutionAuditEvent value, CancellationToken cancellationToken = default)
    {
        DateTimeOffset createdAt = clock.GetUtcNow();
        await context.AuditOutbox.AddAsync(new SolutionsAuditOutboxRecord
        {
            EventId = value.EventId,
            ActorKind = value.ActorKind,
            ActorId = value.ActorId,
            SubjectId = value.SubjectId,
            CorrelationId = value.CorrelationId,
            OriginatingSubjectKind = value.OriginatingSubjectKind,
            EventType = value.EventType,
            WorkspaceId = value.WorkspaceId,
            SolutionVersionId = value.SolutionVersionId,
            InstallationId = value.InstallationId,
            OperationId = value.OperationId,
            Outcome = value.Outcome,
            ProblemCode = value.ProblemCode,
            OccurredAt = value.OccurredAt,
            CreatedAt = createdAt,
            NextAttemptAt = createdAt,
        }, cancellationToken);
    }

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        context.AuditOutbox
            .AsNoTracking()
            .AnyAsync(record => record.EventId == eventId, cancellationToken);
}
