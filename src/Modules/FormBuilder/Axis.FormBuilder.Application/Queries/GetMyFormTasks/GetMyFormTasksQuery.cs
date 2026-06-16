using Axis.FormBuilder.Domain.Enums;
using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Queries.GetMyFormTasks;

/// <summary>List form tasks assigned to the current user.</summary>
public sealed record GetMyFormTasksQuery(
    Guid UserId,
    Guid TeamAccountId,
    FormSubmissionStatus Status) : IQuery<IReadOnlyList<FormTaskSummaryDto>>;
