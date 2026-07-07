using FluentValidation;

namespace Axis.Objects.Application.Queries.ListObjectDefinitions;

public sealed class ListObjectDefinitionsQueryValidator : AbstractValidator<ListObjectDefinitionsQuery>
{
    public ListObjectDefinitionsQueryValidator()
    {
        RuleFor(query => query.Page)
            .GreaterThan(0)
            .WithErrorCode(ObjectsProblemCodes.ObjectDefinitionInvalid);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .WithErrorCode(ObjectsProblemCodes.ObjectDefinitionInvalid);
    }
}
