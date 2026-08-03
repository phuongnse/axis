using FluentValidation;

namespace Axis.BusinessObjects.Application.Commands.SaveBusinessObjectRecord;

public sealed class SaveBusinessObjectRecordCommandValidator
    : AbstractValidator<SaveBusinessObjectRecordCommand>
{
    public SaveBusinessObjectRecordCommandValidator()
    {
        RuleFor(command => command.RecordId).NotEmpty();
        RuleFor(command => command.ExpectedRevision)
            .GreaterThan(0)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectRecordInvalid);
        RuleFor(command => command.Values)
            .NotNull()
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectRecordInvalid);
    }
}
