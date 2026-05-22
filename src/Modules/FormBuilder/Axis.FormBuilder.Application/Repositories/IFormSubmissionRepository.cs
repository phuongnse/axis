using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;

namespace Axis.FormBuilder.Application.Repositories;

public interface IFormSubmissionRepository
{
    Task AddAsync(FormSubmission submission, CancellationToken cancellationToken = default);

    Task<FormSubmission?> GetByAccessTokenAsync(Guid accessToken, CancellationToken cancellationToken = default);

    Task<FormSubmission?> GetByIdAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsForExecutionStepAsync(
        Guid executionId,
        Guid executionStepId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FormSubmission>> GetPendingForUserAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FormSubmission>> GetByUserAndStatusAsync(
        Guid userId,
        Guid organizationId,
        FormSubmissionStatus status,
        CancellationToken cancellationToken = default);
}
