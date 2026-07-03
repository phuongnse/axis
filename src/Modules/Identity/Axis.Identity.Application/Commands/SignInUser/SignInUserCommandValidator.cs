using Axis.Identity.Domain.ValueObjects;
using FluentValidation;

namespace Axis.Identity.Application.Commands.SignInUser;

public sealed class SignInUserCommandValidator : AbstractValidator<SignInUserCommand>
{
    public SignInUserCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .Must(email => Email.Create(email).IsSuccess)
            .WithMessage("Email must be a valid email address.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}
