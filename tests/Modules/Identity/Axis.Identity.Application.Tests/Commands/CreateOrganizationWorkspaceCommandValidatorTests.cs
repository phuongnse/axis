using Axis.Identity.Application;
using Axis.Identity.Application.Commands.CreateOrganizationWorkspace;
using FluentAssertions;
using FluentValidation.Results;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class CreateOrganizationWorkspaceCommandValidatorTests
{
    private readonly CreateOrganizationWorkspaceCommandValidator _validator = new();

    [Theory]
    [InlineData("Acme")]
    [InlineData("  Acme  ")]
    public void Validate_WhenNameIsValid_AllowsCommand(string name)
    {
        ValidationResult result = _validator.Validate(Command(name));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public void Validate_WhenNameIsInvalid_ReturnsFieldError(string name)
    {
        ValidationResult result = _validator.Validate(Command(name));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error =>
            error.PropertyName == "Name"
            && (error.ErrorCode == IdentityProblemCodes.CreateOrganizationNameRequired
                || error.ErrorCode == IdentityProblemCodes.CreateOrganizationNameLength));
    }

    private static CreateOrganizationWorkspaceCommand Command(string name) =>
        new(Guid.NewGuid(), name, "retry-key", "correlation-id");
}
