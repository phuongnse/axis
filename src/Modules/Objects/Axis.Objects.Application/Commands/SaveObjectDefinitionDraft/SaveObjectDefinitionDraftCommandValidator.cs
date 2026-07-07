using FluentValidation;

namespace Axis.Objects.Application.Commands.SaveObjectDefinitionDraft;

public sealed class SaveObjectDefinitionDraftCommandValidator
    : AbstractValidator<SaveObjectDefinitionDraftCommand>
{
    public SaveObjectDefinitionDraftCommandValidator()
    {
        RuleFor(command => command.ObjectDefinitionId)
            .NotEmpty()
            .WithErrorCode(ObjectsProblemCodes.ObjectDefinitionInvalid);

        RuleFor(command => command.ExpectedDraftVersion)
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
