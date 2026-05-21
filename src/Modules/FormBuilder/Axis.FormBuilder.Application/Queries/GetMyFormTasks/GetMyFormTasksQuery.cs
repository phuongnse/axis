using Axis.Shared.Application.CQRS;
using Axis.FormBuilder.Domain.Enums;

namespace Axis.FormBuilder.Application.Queries.GetMyFormTasks;

/// <summary>US-088: List form tasks assigned to the current user.</summary>
public sealed record GetMyFormTasksQuery(
    Guid UserId,
    Guid OrganizationId,
    FormSubmissionStatus Status) : IQuery<IReadOnlyList<FormTaskSummaryDto>>;
