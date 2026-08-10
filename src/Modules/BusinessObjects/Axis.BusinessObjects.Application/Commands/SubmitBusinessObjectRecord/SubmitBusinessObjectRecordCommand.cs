using Axis.Shared.Application.CQRS;

namespace Axis.BusinessObjects.Application.Commands.SubmitBusinessObjectRecord;

public sealed record SubmitBusinessObjectRecordCommand(Guid RecordId, int ExpectedRevision, string? CorrelationId = null)
    : ICommand<BusinessObjectRecordSubmitResultDto>;
