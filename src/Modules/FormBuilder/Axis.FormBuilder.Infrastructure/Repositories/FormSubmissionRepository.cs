using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.FormBuilder.Infrastructure.Repositories;

internal sealed class FormSubmissionRepository(FormBuilderDbContext context) : IFormSubmissionRepository
{
    public async Task AddAsync(FormSubmission submission, CancellationToken cancellationToken = default)
        => await context.FormSubmissions.AddAsync(submission, cancellationToken);

    public async Task<FormSubmission?> GetByAccessTokenAsync(
        Guid accessToken,
        CancellationToken cancellationToken = default)
        => await context.FormSubmissions
            .FirstOrDefaultAsync(s => s.AccessToken == accessToken, cancellationToken);

    public async Task<FormSubmission?> GetByIdAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default)
        => await context.FormSubmissions
            .FirstOrDefaultAsync(
                s => s.Id == id && s.OrganizationId == organizationId,
                cancellationToken);

    public async Task<bool> ExistsForExecutionStepAsync(
        Guid executionId,
        Guid executionStepId,
        CancellationToken cancellationToken = default)
        => await context.FormSubmissions
            .AnyAsync(
                s => s.ExecutionId == executionId && s.ExecutionStepId == executionStepId,
                cancellationToken);

    public async Task<IReadOnlyList<FormSubmission>> GetPendingForUserAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
        => await context.FormSubmissions
            .Where(s => s.OrganizationId == organizationId
                && s.Status == FormSubmissionStatus.Pending
                && s.AssigneeUserId == userId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FormSubmission>> GetByUserAndStatusAsync(
        Guid userId,
        Guid organizationId,
        FormSubmissionStatus status,
        CancellationToken cancellationToken = default)
        => await context.FormSubmissions
            .Where(s => s.OrganizationId == organizationId
                && s.Status == status
                && (s.AssigneeUserId == userId || s.SubmittedByUserId == userId))
            .OrderByDescending(s => s.SubmittedAt ?? s.CreatedAt)
            .ToListAsync(cancellationToken);
}
