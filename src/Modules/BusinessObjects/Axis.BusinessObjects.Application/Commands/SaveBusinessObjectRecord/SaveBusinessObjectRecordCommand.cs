using Axis.Shared.Application.CQRS;

namespace Axis.BusinessObjects.Application.Commands.SaveBusinessObjectRecord;

public sealed record SaveBusinessObjectRecordCommand(
    Guid RecordId,
    int ExpectedRevision,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values,
    string? CorrelationId = null)
    : ICommand<BusinessObjectRecordDetailDto>;
