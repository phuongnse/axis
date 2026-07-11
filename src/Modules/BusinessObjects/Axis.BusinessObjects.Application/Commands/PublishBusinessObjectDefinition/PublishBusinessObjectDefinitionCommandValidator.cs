using FluentValidation;

namespace Axis.BusinessObjects.Application.Commands.PublishBusinessObjectDefinition;

public sealed class PublishBusinessObjectDefinitionCommandValidator
    : AbstractValidator<PublishBusinessObjectDefinitionCommand>
{
    public PublishBusinessObjectDefinitionCommandValidator()
    {
        RuleFor(command => command.BusinessObjectDefinitionId)
            .NotEmpty()
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);

        RuleFor(command => command.ExpectedRevision)
            .GreaterThan(0)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);
    }
}
