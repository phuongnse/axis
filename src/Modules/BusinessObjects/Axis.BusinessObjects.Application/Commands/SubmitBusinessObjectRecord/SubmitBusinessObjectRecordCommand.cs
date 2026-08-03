using Axis.Shared.Application.CQRS;

namespace Axis.BusinessObjects.Application.Commands.SubmitBusinessObjectRecord;

public sealed record SubmitBusinessObjectRecordCommand(Guid RecordId, int ExpectedRevision)
    : ICommand<BusinessObjectRecordSubmitResultDto>;
