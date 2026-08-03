using FluentValidation;

namespace Axis.BusinessObjects.Application.Commands.CreateBusinessObjectRecord;

public sealed class CreateBusinessObjectRecordCommandValidator
    : AbstractValidator<CreateBusinessObjectRecordCommand>
{
    public CreateBusinessObjectRecordCommandValidator()
    {
        RuleFor(command => command.ObjectKey)
            .NotEmpty()
            .MaximumLength(63)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectRecordInvalid);
        RuleFor(command => command.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(120)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectRecordInvalid);
        RuleFor(command => command.Values)
            .NotNull()
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectRecordInvalid);
    }
}
