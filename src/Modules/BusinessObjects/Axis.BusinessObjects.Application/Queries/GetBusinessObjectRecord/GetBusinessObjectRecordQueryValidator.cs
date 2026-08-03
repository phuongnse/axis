using FluentValidation;

namespace Axis.BusinessObjects.Application.Queries.GetBusinessObjectRecord;

public sealed class GetBusinessObjectRecordQueryValidator : AbstractValidator<GetBusinessObjectRecordQuery>
{
    public GetBusinessObjectRecordQueryValidator() => RuleFor(query => query.RecordId).NotEmpty();
}
