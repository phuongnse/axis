using Axis.FormBuilder.Application.Queries.GetFormById;

namespace Axis.FormBuilder.Application.Queries.GetFormTaskByToken;

public sealed record FormTaskByTokenDto(
    Guid SubmissionId,
    string Status,
    Guid FormDefinitionId,
    string FormName,
    string? FormDescription,
    IReadOnlyList<FormFieldDto> Fields,
    DateTimeOffset? ExpiresAt);
