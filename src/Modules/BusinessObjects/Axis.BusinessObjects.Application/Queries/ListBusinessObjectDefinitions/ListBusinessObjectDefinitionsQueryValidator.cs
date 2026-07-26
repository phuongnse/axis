using FluentValidation;

namespace Axis.BusinessObjects.Application.Queries.ListBusinessObjectDefinitions;

public sealed class ListBusinessObjectDefinitionsQueryValidator : AbstractValidator<ListBusinessObjectDefinitionsQuery>
{
    public ListBusinessObjectDefinitionsQueryValidator()
    {
        RuleFor(query => query.Page)
            .GreaterThan(0)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);

        RuleFor(query => query.SearchQuery)
            .MaximumLength(200)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);

        RuleFor(query => query.Language)
            .MaximumLength(16)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);
    }
}
