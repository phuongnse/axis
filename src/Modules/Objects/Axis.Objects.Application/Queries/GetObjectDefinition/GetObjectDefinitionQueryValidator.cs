using FluentValidation;

namespace Axis.Objects.Application.Queries.GetObjectDefinition;

public sealed class GetObjectDefinitionQueryValidator : AbstractValidator<GetObjectDefinitionQuery>
{
    public GetObjectDefinitionQueryValidator()
    {
        RuleFor(query => query.ObjectDefinitionId)
            .NotEmpty()
            .WithErrorCode(ObjectsProblemCodes.ObjectDefinitionInvalid);
    }
}
