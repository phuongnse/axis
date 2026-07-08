using FluentValidation;

namespace Axis.Objects.Application.Commands.SaveUnpublishedObjectDefinition;

public sealed class SaveUnpublishedObjectDefinitionCommandValidator
    : AbstractValidator<SaveUnpublishedObjectDefinitionCommand>
{
    public SaveUnpublishedObjectDefinitionCommandValidator()
    {
        RuleFor(command => command.ObjectDefinitionId)
            .NotEmpty()
            .WithErrorCode(ObjectsProblemCodes.ObjectDefinitionInvalid);

        RuleFor(command => command.ExpectedRevision)
            .GreaterThan(0)
            .WithErrorCode(ObjectsProblemCodes.ObjectDefinitionInvalid);

        RuleFor(command => command.Name)
            .NotEmpty()
            .WithErrorCode(ObjectsProblemCodes.ObjectDefinitionInvalid);

        RuleFor(command => command.Fields)
            .NotNull()
            .WithErrorCode(ObjectsProblemCodes.ObjectDefinitionInvalid);
    }
}
