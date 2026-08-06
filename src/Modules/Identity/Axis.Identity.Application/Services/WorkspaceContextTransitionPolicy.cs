namespace Axis.Identity.Application.Services;

public sealed record WorkspaceContextTransitionPolicy(
    TimeSpan ConfirmationLifetime,
    TimeSpan RetentionLifetime)
{
    public WorkspaceContextTransitionPolicy Validate()
    {
        if (ConfirmationLifetime <= TimeSpan.Zero)
            throw new InvalidOperationException("Workspace transition confirmation lifetime must be positive.");
        if (RetentionLifetime < ConfirmationLifetime)
            throw new InvalidOperationException("Workspace transition retention must outlive confirmation.");

        return this;
    }
}
