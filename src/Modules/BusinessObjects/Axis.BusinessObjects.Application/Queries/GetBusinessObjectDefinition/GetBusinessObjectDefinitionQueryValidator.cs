using FluentValidation;

namespace Axis.BusinessObjects.Application.Queries.GetBusinessObjectDefinition;

public sealed class GetBusinessObjectDefinitionQueryValidator : AbstractValidator<GetBusinessObjectDefinitionQuery>
{
    public GetBusinessObjectDefinitionQueryValidator()
    {
        RuleFor(query => query.BusinessObjectDefinitionId)
            .NotEmpty()
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);
    }
}
