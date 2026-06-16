namespace Axis.FormBuilder.Application.Messages;

/// <summary>Scheduled job payload to expire a pending form task at its due time.</summary>
public sealed record ExpireFormSubmissionMessage(Guid FormSubmissionId, Guid tenantId);
