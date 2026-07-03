using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.SignInUser;

public sealed record SignInUserCommand(
    string Email,
    string Password) : ICommand<SignInSuccessDto>;
