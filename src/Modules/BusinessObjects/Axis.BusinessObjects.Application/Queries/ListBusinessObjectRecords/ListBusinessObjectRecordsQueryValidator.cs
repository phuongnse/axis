using FluentValidation;

namespace Axis.BusinessObjects.Application.Queries.ListBusinessObjectRecords;

public sealed class ListBusinessObjectRecordsQueryValidator
    : AbstractValidator<ListBusinessObjectRecordsQuery>
{
    public ListBusinessObjectRecordsQueryValidator()
    {
        RuleFor(query => query.Page)
            .GreaterThan(0)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectRecordInvalid);
        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectRecordInvalid);
        RuleFor(query => query.ObjectKey)
            .MaximumLength(63)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectRecordInvalid);
    }
}
