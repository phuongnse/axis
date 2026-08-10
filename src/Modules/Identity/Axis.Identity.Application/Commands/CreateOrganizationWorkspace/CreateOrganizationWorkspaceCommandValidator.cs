using Axis.Identity.Domain.Aggregates;
using FluentValidation;

namespace Axis.Identity.Application.Commands.CreateOrganizationWorkspace;

public sealed class CreateOrganizationWorkspaceCommandValidator
    : AbstractValidator<CreateOrganizationWorkspaceCommand>
{
    public CreateOrganizationWorkspaceCommandValidator()
    {
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Organization name is required.")
            .WithErrorCode(IdentityProblemCodes.CreateOrganizationNameRequired)
            .Must(name =>
            {
                int length = name.Trim().Normalize().Length;
                return length is >= Organization.MinNameLength and <= Organization.MaxNameLength;
            })
            .WithMessage(
                $"Organization name must be between {Organization.MinNameLength} and {Organization.MaxNameLength} characters.")
            .WithErrorCode(IdentityProblemCodes.CreateOrganizationNameLength);
    }
}
