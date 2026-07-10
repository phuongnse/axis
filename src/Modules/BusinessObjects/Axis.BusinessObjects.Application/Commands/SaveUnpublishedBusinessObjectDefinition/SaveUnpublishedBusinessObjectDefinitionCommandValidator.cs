using FluentValidation;

namespace Axis.BusinessObjects.Application.Commands.SaveUnpublishedBusinessObjectDefinition;

public sealed class SaveUnpublishedBusinessObjectDefinitionCommandValidator
    : AbstractValidator<SaveUnpublishedBusinessObjectDefinitionCommand>
{
    public SaveUnpublishedBusinessObjectDefinitionCommandValidator()
    {
        RuleFor(command => command.BusinessObjectDefinitionId)
            .NotEmpty()
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);

        RuleFor(command => command.ExpectedRevision)
            .GreaterThan(0)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);

        RuleFor(command => command.Name)
            .NotEmpty()
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);

        RuleFor(command => command.Fields)
            .NotNull()
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);
    }
}
