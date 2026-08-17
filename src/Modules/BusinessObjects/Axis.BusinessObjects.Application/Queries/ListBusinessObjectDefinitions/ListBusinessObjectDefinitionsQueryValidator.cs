using FluentValidation;
using Axis.Shared.Application;

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

        RuleFor(query => query.SortBy)
            .IsInEnum()
            .When(query => query.SortBy.HasValue)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);

        RuleFor(query => query.SortDirection)
            .IsInEnum()
            .When(query => query.SortDirection.HasValue)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);

        RuleFor(query => query.SortDirection)
            .Null()
            .When(query => !query.SortBy.HasValue)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);

        RuleFor(query => query.SortDirection)
            .NotNull()
            .When(query => query.SortBy.HasValue)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);
    }
}
