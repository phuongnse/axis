namespace Axis.FormBuilder.Application.Queries.GetMyFormTasks;

public sealed record FormTaskSummaryDto(
    Guid Id,
    Guid FormDefinitionId,
    string FormName,
    Guid ExecutionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    Guid AccessToken);
