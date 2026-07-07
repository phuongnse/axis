using FluentValidation;

namespace Axis.Objects.Application.Commands.CreateObjectDefinitionDraft;

public sealed class CreateObjectDefinitionDraftCommandValidator
    : AbstractValidator<CreateObjectDefinitionDraftCommand>
{
    public CreateObjectDefinitionDraftCommandValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty()
            .WithErrorCode(ObjectsProblemCodes.ObjectDefinitionInvalid);
    }
}
