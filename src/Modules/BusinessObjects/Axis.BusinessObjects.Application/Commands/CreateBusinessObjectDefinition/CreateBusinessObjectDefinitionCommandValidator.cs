using FluentValidation;

namespace Axis.BusinessObjects.Application.Commands.CreateBusinessObjectDefinition;

public sealed class CreateBusinessObjectDefinitionCommandValidator
    : AbstractValidator<CreateBusinessObjectDefinitionCommand>
{
    public CreateBusinessObjectDefinitionCommandValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty()
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);
    }
}
