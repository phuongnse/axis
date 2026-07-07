using FluentValidation;

namespace Axis.Objects.Application.Commands.PublishObjectDefinition;

public sealed class PublishObjectDefinitionCommandValidator
    : AbstractValidator<PublishObjectDefinitionCommand>
{
    public PublishObjectDefinitionCommandValidator()
    {
        RuleFor(command => command.ObjectDefinitionId)
            .NotEmpty()
            .WithErrorCode(ObjectsProblemCodes.ObjectDefinitionInvalid);

        RuleFor(command => command.ExpectedDraftVersion)
            .GreaterThan(0)
            .WithErrorCode(ObjectsProblemCodes.ObjectDefinitionInvalid);
    }
}
