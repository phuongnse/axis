namespace Axis.Identity.Application.Commands.SignInUser;

public sealed record SignInSuccessDto(
    Guid UserId,
    Guid? workspaceId,
    string Email,
    string FullName,
    SignInNextStep NextStep);

public sealed record SignInSessionEstablishedDto(
    bool SessionEstablished,
    SignInNextStep NextStep)
{
    public static SignInSessionEstablishedDto From(SignInSuccessDto signIn) =>
        new(true, signIn.NextStep);
}

public enum SignInNextStep
{
    Dashboard
}
