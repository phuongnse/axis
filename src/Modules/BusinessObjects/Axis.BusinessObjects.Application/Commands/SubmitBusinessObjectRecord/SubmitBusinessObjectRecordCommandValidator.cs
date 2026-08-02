using FluentValidation;

namespace Axis.BusinessObjects.Application.Commands.SubmitBusinessObjectRecord;

public sealed class SubmitBusinessObjectRecordCommandValidator
    : AbstractValidator<SubmitBusinessObjectRecordCommand>
{
    public SubmitBusinessObjectRecordCommandValidator()
    {
        RuleFor(command => command.RecordId).NotEmpty();
        RuleFor(command => command.ExpectedRevision)
            .GreaterThan(0)
            .WithErrorCode(BusinessObjectsProblemCodes.BusinessObjectRecordInvalid);
    }
}
