using FluentValidation;

namespace Axis.Objects.Application.Commands.CreateObjectDefinition;

public sealed class CreateObjectDefinitionCommandValidator
    : AbstractValidator<CreateObjectDefinitionCommand>
{
    public CreateObjectDefinitionCommandValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty()
            .WithErrorCode(ObjectsProblemCodes.ObjectDefinitionInvalid);
    }
}
