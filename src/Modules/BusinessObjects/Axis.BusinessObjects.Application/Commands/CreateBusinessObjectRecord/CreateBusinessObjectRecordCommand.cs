using Axis.Shared.Application.CQRS;

namespace Axis.BusinessObjects.Application.Commands.CreateBusinessObjectRecord;

public sealed record CreateBusinessObjectRecordCommand(
    string ObjectKey,
    string IdempotencyKey,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values)
    : ICommand<BusinessObjectRecordDetailDto>;
